using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Domain.Entities;

/// <summary>
/// The persisted progress of one post-run rebase (ADR 0087), keyed by repository and issue number and
/// scoped to one rebase-pending episode (<see cref="EnteredRebasePendingAt"/>). Persisting it lets the rebase
/// step resume after a host restart: the pre-rebase head it waits to see replaced, when it asked the provider
/// to rebase, and its transient-failure backoff.
/// </summary>
public sealed class PendingRebase
{
    /// <summary>Transient provider failures tolerated before the rebase resolves <c>Skipped(provider-error)</c>.</summary>
    public const int MaxTransientFailures = 5;

    /// <summary>How long Foundry waits for a requested rebase to land as a new head before skipping it.</summary>
    public static readonly TimeSpan LandingTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Delay between reads of the head while waiting for a requested rebase to land.</summary>
    public static readonly TimeSpan LandingPollDelay = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10);

    // Private parameterless constructor for EF Core materialization.
    private PendingRebase()
    {
    }

    public MonitoredRepositoryId MonitoredRepositoryId { get; private set; }

    public int IssueNumber { get; private set; }

    /// <summary>When the issue entered rebase-pending; a different value means a new episode.</summary>
    public DateTimeOffset EnteredRebasePendingAt { get; private set; }

    /// <summary>The branch head the rebase was requested against; set once the provider accepted the request.</summary>
    public string? PreRebaseHeadSha { get; private set; }

    /// <summary>When the provider accepted the rebase request.</summary>
    public DateTimeOffset? RequestedAt { get; private set; }

    public int TransientFailures { get; private set; }

    /// <summary>Not before this time does the step retry after a transient failure.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>When the step resolved an outcome (or saw the pull request close) for this episode.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    public bool IsResolved => ResolvedAt is not null;

    public bool IsAwaitingLanding => RequestedAt is not null;

    public static PendingRebase Start(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset enteredRebasePendingAt) =>
        new()
        {
            MonitoredRepositoryId = repositoryId,
            IssueNumber = issueNumber,
            EnteredRebasePendingAt = enteredRebasePendingAt,
        };

    public bool IsForEpisode(DateTimeOffset enteredRebasePendingAt) =>
        EnteredRebasePendingAt == enteredRebasePendingAt;

    /// <summary>Discards the progress of an earlier episode and starts tracking a new one.</summary>
    public void Restart(DateTimeOffset enteredRebasePendingAt)
    {
        EnteredRebasePendingAt = enteredRebasePendingAt;
        PreRebaseHeadSha = null;
        RequestedAt = null;
        TransientFailures = 0;
        NextAttemptAt = null;
        ResolvedAt = null;
    }

    public bool IsDue(DateTimeOffset now) => NextAttemptAt is not { } next || next <= now;

    public void RecordRequested(string preRebaseHeadSha, DateTimeOffset requestedAt)
    {
        PreRebaseHeadSha = preRebaseHeadSha;
        RequestedAt = requestedAt;
        NextAttemptAt = requestedAt + LandingPollDelay;
    }

    /// <summary>Waits <see cref="LandingPollDelay"/> before reading the head again.</summary>
    public void RecordAwaitingLanding(DateTimeOffset now)
    {
        NextAttemptAt = now + LandingPollDelay;
    }

    public bool HasLandingTimedOut(DateTimeOffset now) =>
        RequestedAt is { } requestedAt && now - requestedAt >= LandingTimeout;

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
