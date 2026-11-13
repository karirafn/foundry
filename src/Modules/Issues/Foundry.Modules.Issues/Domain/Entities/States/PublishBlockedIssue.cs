using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using FailureCategoryVO = Foundry.Modules.Workers.Contracts.FailureCategory;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// The Diff gate found possible secrets in the run's commits, so nothing was pushed (#788). The run is not terminal: its
/// unpublished work waits in its Hand-off volume until an operator discards the run or the volume's time to live does.
/// There is no way to publish it anyway, and Retry Issue is not offered until the run is discarded.
/// </summary>
public sealed class PublishBlockedIssue : Issue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => true;

    public override bool AwaitsDispatch => false;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private PublishBlockedIssue()
    {
    }

    private PublishBlockedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    /// <summary>The revised pull request; empty for an implement run.</summary>
    public string PullRequestUrl { get; private set; } = string.Empty;

    public override string? LinkedPullRequestUrl => NullIfEmpty(PullRequestUrl);

    /// <summary>The revision's trigger, or <see langword="null"/> when the run was an implement run.</summary>
    public RevisionTrigger? Trigger { get; private set; }

    public int ChecksFixAttempts { get; private set; }

    public int RebaseConflictAttempts { get; private set; }

    public bool IsRevision => Trigger is not null;

    public DateTimeOffset BlockedAt { get; private set; }

    internal static PublishBlockedIssue FromInProgress(InProgressIssue source, string branchName, DateTimeOffset blockedAt)
    {
        PublishBlockedIssue blocked = new(source.Id, blockedAt);
        blocked.CopySharedPropertiesFrom(source);
        blocked.WorkerRunId = source.WorkerRunId;
        blocked.BranchName = branchName;
        blocked.BlockedAt = blockedAt;
        return blocked;
    }

    internal static PublishBlockedIssue FromRevisionInProgress(RevisionInProgressIssue source, DateTimeOffset blockedAt)
    {
        PublishBlockedIssue blocked = new(source.Id, blockedAt);
        blocked.CopySharedPropertiesFrom(source);
        blocked.WorkerRunId = source.WorkerRunId;
        blocked.BranchName = source.BranchName;
        blocked.PullRequestUrl = source.PullRequestUrl;
        blocked.Trigger = source.Trigger;
        blocked.ChecksFixAttempts = source.ChecksFixAttempts;
        blocked.RebaseConflictAttempts = source.RebaseConflictAttempts;
        blocked.BlockedAt = blockedAt;
        return blocked;
    }

    /// <summary>
    /// The run ended without publishing: an operator discarded it, or its time to live did. Retry Issue then starts a
    /// new run from whatever the branch holds.
    /// </summary>
    public Issue Fail(string failureReason, FailureCategoryVO failureCategory, DateTimeOffset failedAt)
    {
        if (Trigger is { } trigger)
        {
            RevisionFailedIssue revisionFailed = RevisionFailedIssue.FromPublishBlocked(
                this,
                trigger,
                failureReason,
                failureCategory,
                failedAt);
            AddDomainEvent(new Events.IssueRevisionFailed(Id, MonitoredRepositoryId));
            return revisionFailed;
        }

        ContinuableFailedIssue failed = ContinuableFailedIssue.FromPublishBlocked(this, failureReason, failureCategory, failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
