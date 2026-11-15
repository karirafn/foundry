using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Domain.Entities;

/// <summary>
/// The persisted progress of Foundry's merge of one pull request (ADR 0105), keyed by repository and issue number
/// and scoped to one merge-pending episode (<see cref="EnteredMergePendingAt"/>). Persisting it lets the merge step
/// keep its transient-failure count and backoff across host restarts.
/// <para>
/// It is also the pull request's entry in its repository's catch-up queue (ADR 0111). An entry is in one of four
/// places:
/// <list type="bullet">
/// <item><b>Not queued</b> (<see cref="QueuedAt"/> unset): the step attempts the pinned merge.</item>
/// <item><b>Waiting</b> (<see cref="AwaitsCatchUp"/>): the merge was refused because the pull request is behind; no
/// merge is attempted until the entry heads the queue and starts a catch-up.</item>
/// <item><b>Catching up</b> (queued, resolved by <see cref="StartCatchUp"/>): the issue rebases and its checks run
/// again; the entry outlives merge-pending to keep its place.</item>
/// <item><b>Caught up</b> (queued, restarted after a catch-up): the step attempts the pinned merge once more.</item>
/// </list>
/// </para>
/// </summary>
public sealed class PendingMerge
{
    /// <summary>Transient provider failures tolerated before the merge resolves <c>skipped(provider_error)</c>.</summary>
    public const int MaxTransientFailures = 5;

    private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10);

    // Private parameterless constructor for EF Core materialization.
    private PendingMerge()
    {
    }

    public MonitoredRepositoryId MonitoredRepositoryId { get; private set; }

    public int IssueNumber { get; private set; }

    /// <summary>When the issue entered merge-pending; a different value means a new episode.</summary>
    public DateTimeOffset EnteredMergePendingAt { get; private set; }

    public int TransientFailures { get; private set; }

    /// <summary>Not before this time does the step retry after a transient failure.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>When the step resolved an outcome (or saw the pull request or issue close) for this episode.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    public bool IsResolved => ResolvedAt is not null;

    /// <summary>
    /// When the entry first joined the catch-up queue; the queue is ordered by it. Kept across catch-up rounds, and
    /// unset while the entry is not queued.
    /// </summary>
    public DateTimeOffset? QueuedAt { get; private set; }

    /// <summary>The catch-ups this queue entry has started.</summary>
    public int CatchUpRounds { get; private set; }

    /// <summary>
    /// Whether the entry waits for its next catch-up: its merge was refused because the pull request is behind, and
    /// no merge is attempted until it starts one.
    /// </summary>
    public bool AwaitsCatchUp { get; private set; }

    public bool IsQueued => QueuedAt is not null;

    public static PendingMerge Start(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset enteredMergePendingAt) =>
        new()
        {
            MonitoredRepositoryId = repositoryId,
            IssueNumber = issueNumber,
            EnteredMergePendingAt = enteredMergePendingAt,
        };

    public bool IsForEpisode(DateTimeOffset enteredMergePendingAt) =>
        EnteredMergePendingAt == enteredMergePendingAt;

    /// <summary>
    /// Discards the progress of an earlier episode and starts tracking a new one. An entry back from a catch-up keeps
    /// its place and rounds and attempts its merge; an entry that left merge-pending while waiting did not catch up,
    /// so it leaves the queue.
    /// </summary>
    public void Restart(DateTimeOffset enteredMergePendingAt)
    {
        EnteredMergePendingAt = enteredMergePendingAt;
        TransientFailures = 0;
        NextAttemptAt = null;
        ResolvedAt = null;
        if (AwaitsCatchUp)
        {
            LeaveQueue();
        }
    }

    public bool IsDue(DateTimeOffset now) => NextAttemptAt is not { } next || next <= now;

    /// <summary>
    /// Records a transient provider failure and backs off exponentially. Returns <see langword="true"/> when the
    /// retry budget is exhausted.
    /// </summary>
    public bool RecordTransientFailure(DateTimeOffset now)
    {
        TransientFailures++;
        if (TransientFailures >= MaxTransientFailures)
        {
            return true;
        }

        double factor = Math.Pow(2, TransientFailures - 1);
        TimeSpan backoff = TimeSpan.FromTicks((long)Math.Min(BaseBackoff.Ticks * factor, MaxBackoff.Ticks));
        NextAttemptAt = now + backoff;
        return false;
    }

    /// <summary>
    /// Records the episode's final outcome. The issue leaves the merge path, so the entry leaves the catch-up queue;
    /// a later merge-pending episode joins at the back with no rounds used.
    /// </summary>
    public void MarkResolved(DateTimeOffset now)
    {
        ResolvedAt = now;
        NextAttemptAt = null;
        LeaveQueue();
    }

    /// <summary>
    /// The merge was refused because the pull request is behind: the entry joins the queue, or keeps its place when
    /// it already holds one, and waits for its next catch-up. The round cap is the queue's rule
    /// (<see cref="MergeQueue.RefuseBehind"/>).
    /// </summary>
    internal void AwaitCatchUp(DateTimeOffset now)
    {
        QueuedAt ??= now;
        AwaitsCatchUp = true;
    }

    /// <summary>
    /// The waiting entry heads the queue and starts a catch-up round: the episode resolves, and the entry keeps its
    /// place while the issue rebases and its checks run again.
    /// </summary>
    /// <exception cref="InvalidOperationException">The entry is not waiting for a catch-up.</exception>
    internal void StartCatchUp(DateTimeOffset now)
    {
        if (!AwaitsCatchUp)
        {
            throw new InvalidOperationException(
                $"Pending merge of issue #{IssueNumber} is not waiting for a catch-up.");
        }

        CatchUpRounds++;
        AwaitsCatchUp = false;
        ResolvedAt = now;
        NextAttemptAt = null;
    }

    private void LeaveQueue()
    {
        QueuedAt = null;
        CatchUpRounds = 0;
        AwaitsCatchUp = false;
    }
}
