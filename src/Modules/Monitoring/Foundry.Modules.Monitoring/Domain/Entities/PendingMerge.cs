using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Domain.Entities;

/// <summary>
/// The persisted progress of Foundry's merge of one pull request (ADR 0105), keyed by repository and issue number
/// and scoped to one merge-pending episode (<see cref="EnteredMergePendingAt"/>). Persisting it lets the merge step
/// keep its transient-failure count and backoff across host restarts.
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

    /// <summary>Discards the progress of an earlier episode and starts tracking a new one.</summary>
    public void Restart(DateTimeOffset enteredMergePendingAt)
    {
        EnteredMergePendingAt = enteredMergePendingAt;
        TransientFailures = 0;
        NextAttemptAt = null;
        ResolvedAt = null;
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

    public void MarkResolved(DateTimeOffset now)
    {
        ResolvedAt = now;
        NextAttemptAt = null;
    }
}
