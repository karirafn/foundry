using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features.Webhooks.Nudges;

/// <summary>
/// Coalesces and rate-caps nudges for the single-consumer poll loop (ADR 0085). Not thread-safe:
/// owned and driven only by <see cref="Polling.MonitoringService"/>.
/// <list type="bullet">
/// <item>Nudges for the same <see cref="NudgeKey"/> that are pending together run once.</item>
/// <item>An event-triggered nudge runs a key at most once per <see cref="RateFloor"/>: a nudge less than
/// <see cref="RateFloor"/> after the key's previous event-triggered run joins a single trailing run at last
/// run + <see cref="RateFloor"/>; a nudge exactly <see cref="RateFloor"/> later runs immediately.</item>
/// <item>A not-before (deadline) nudge runs at its deadline, outside the floor, and does not stamp the
/// floor window; a floored event-triggered nudge pending for the same key runs with it. Each deadline
/// producer bounds its own re-arming.</item>
/// </list>
/// </summary>
internal sealed class NudgeScheduler
{
    /// <summary>
    /// How long the poll loop waits after a nudge wakes it before draining the channel, so a burst
    /// of deliveries for one key runs the pass once.
    /// </summary>
    internal static readonly TimeSpan CoalescingWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The shortest time between two event-triggered runs of one nudge key, independent of the repository's
    /// poll interval: webhooks are the primary change signal, so their changes must appear within seconds.
    /// </summary>
    internal static readonly TimeSpan RateFloor = TimeSpan.FromSeconds(10);

    /// <summary>Last-run records older than this are forgotten; the floor is far shorter.</summary>
    private static readonly TimeSpan LastRunRetention = TimeSpan.FromDays(1);

    private readonly Dictionary<NudgeKey, PendingNudge> _pending = [];
    private readonly Dictionary<NudgeKey, DateTimeOffset> _lastRun = [];
    private readonly List<Nudge> _deferred = [];

    internal int PendingCount => _pending.Count + _deferred.Count;

    /// <summary>
    /// The earliest time a pending or deferred nudge may become due, as of the last <see cref="TakeDue"/>
    /// (or <see cref="Add"/>); <see langword="null"/> when nothing is waiting.
    /// </summary>
    internal DateTimeOffset? NextWakeAt { get; private set; }

    public void Add(Nudge nudge, DateTimeOffset receivedAt)
    {
        if (nudge.NotBefore is { } notBefore)
        {
            if (notBefore > receivedAt)
            {
                // Identical deferred nudges (e.g. repeated review passes over the same held comment) run once anyway.
                if (_deferred.Contains(nudge))
                {
                    return;
                }

                _deferred.Add(nudge);
                NextWakeAt = Earliest(NextWakeAt, notBefore);
                return;
            }

            AddPending(nudge.Key, notBefore, deadlineReached: true);
            return;
        }

        AddPending(nudge.Key, receivedAt, deadlineReached: false);
    }

    /// <summary>
    /// Removes and returns every key due at <paramref name="now"/>, recording each event-triggered run.
    /// <paramref name="isMonitored"/> is <see langword="false"/> for a repository that is no longer
    /// monitored (its nudges are dropped).
    /// </summary>
    public IReadOnlyList<NudgeKey> TakeDue(
        DateTimeOffset now,
        Func<MonitoredRepositoryId, bool> isMonitored)
    {
        for (int i = _deferred.Count - 1; i >= 0; i--)
        {
            Nudge deferred = _deferred[i];
            if (deferred.NotBefore is { } notBefore && notBefore <= now)
            {
                _deferred.RemoveAt(i);
                AddPending(deferred.Key, notBefore, deadlineReached: true);
            }
        }

        List<NudgeKey> due = [];
        DateTimeOffset? nextWake = null;

        foreach ((NudgeKey key, PendingNudge pending) in _pending.ToList())
        {
            if (!isMonitored(key.RepositoryId))
            {
                _pending.Remove(key);
                continue;
            }

            if (pending.DeadlineReached)
            {
                // A deadline run leaves the floor window alone, so an event arriving just after it is not deferred.
                _pending.Remove(key);
                due.Add(key);
                continue;
            }

            DateTimeOffset dueAt = DueAt(key, pending.ReceivedAt);
            if (dueAt <= now)
            {
                _pending.Remove(key);
                _lastRun[key] = now;
                due.Add(key);
            }
            else
            {
                nextWake = Earliest(nextWake, dueAt);
            }
        }

        foreach (Nudge deferred in _deferred)
        {
            nextWake = Earliest(nextWake, deferred.NotBefore ?? now);
        }

        foreach (KeyValuePair<NudgeKey, DateTimeOffset> stale in _lastRun.Where(e => now - e.Value > LastRunRetention).ToList())
        {
            _lastRun.Remove(stale.Key);
        }

        NextWakeAt = nextWake;
        return due;
    }

    private void AddPending(NudgeKey key, DateTimeOffset receivedAt, bool deadlineReached)
    {
        if (_pending.TryGetValue(key, out PendingNudge existing))
        {
            if (deadlineReached && !existing.DeadlineReached)
            {
                _pending[key] = existing with { DeadlineReached = true };
                NextWakeAt = Earliest(NextWakeAt, receivedAt);
            }

            return;
        }

        _pending[key] = new PendingNudge(receivedAt, deadlineReached);
        NextWakeAt = Earliest(NextWakeAt, receivedAt);
    }

    private DateTimeOffset DueAt(NudgeKey key, DateTimeOffset receivedAt)
    {
        if (_lastRun.TryGetValue(key, out DateTimeOffset lastRun) && lastRun + RateFloor > receivedAt)
        {
            return lastRun + RateFloor;
        }

        return receivedAt;
    }

    private static DateTimeOffset Earliest(DateTimeOffset? current, DateTimeOffset candidate)
        => current is { } value && value <= candidate ? value : candidate;

    /// <param name="DeadlineReached">A not-before nudge whose deadline passed contributed to this entry.</param>
    private readonly record struct PendingNudge(DateTimeOffset ReceivedAt, bool DeadlineReached);
}
