using System.Collections.Concurrent;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Features.Dispatch;

/// <summary>
/// Decides whether a running worker's per-tick branch compare runs (issue #660, ADR 0085).
/// <para>
/// While the run's repository has an <c>Active</c> webhook, pushes announce every commit, so the compare
/// runs only when a <see cref="ProviderBranchPushed"/> is pending for the run's branch — plus once on
/// the run's first observation, which covers a push that arrived before the run was recorded as running.
/// For any other Webhook Status the compare runs on every tick, exactly as before.
/// </para>
/// <para>
/// Pushes are only recorded here; <see cref="WorkerDispatchService"/> remains the single caller of the
/// compare, so several pushes between two ticks coalesce into one compare per run per tick interval.
/// </para>
/// Singleton: <see cref="RecordPush"/> is called from webhook requests, the rest from the dispatch loop.
/// </summary>
internal sealed class RunningBranchCompareGate
{
    private readonly ConcurrentDictionary<(MonitoredRepositoryId RepositoryId, string Branch), byte> _pendingPushes = new();

    // Touched only by the single-threaded dispatch loop.
    private readonly HashSet<WorkerRunId> _observedRuns = [];

    /// <summary>Records a push to <paramref name="branch"/>; the next tick compares that branch's run.</summary>
    public void RecordPush(MonitoredRepositoryId repositoryId, string branch)
        => _pendingPushes[(repositoryId, branch)] = 0;

    /// <summary>Whether a push to <paramref name="branch"/> is waiting for the next tick.</summary>
    internal bool IsPushPending(MonitoredRepositoryId repositoryId, string branch)
        => _pendingPushes.ContainsKey((repositoryId, branch));

    /// <summary>
    /// Whether the run's compare should run this tick. Consumes a pending push for the run's branch.
    /// </summary>
    public bool ShouldCompare(
        WorkerRunId runId,
        MonitoredRepositoryId repositoryId,
        string branch,
        bool webhookActive)
    {
        // A push recorded before this decision is covered by the compare that follows (or, when the hook
        // is not Active, by the per-tick compare anyway), so it is always consumed here.
        bool pushPending = _pendingPushes.TryRemove((repositoryId, branch), out _);
        bool firstObservation = _observedRuns.Add(runId);

        return !webhookActive || pushPending || firstObservation;
    }

    /// <summary>Forgets runs that are no longer active, so the observed set stays bounded.</summary>
    public void RetainRuns(IReadOnlyCollection<WorkerRunId> activeRunIds)
    {
        _observedRuns.IntersectWith(activeRunIds);
    }
}
