using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using FailureCategoryVO = Foundry.Modules.Workers.Contracts.FailureCategory;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// The run's publish retries ran out (ADR 0100). The run is not terminal: its unpublished work waits in its retained
/// Hand-off volume until an operator retries the publish or the volume's time to live discards it. Retry Issue is not
/// offered here, because a new run would abandon the unpublished one.
/// </summary>
public sealed class PublishFailedIssue : Issue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => true;

    public override bool AwaitsDispatch => false;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private PublishFailedIssue()
    {
    }

    private PublishFailedIssue(IssueId id) : base(id)
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

    public string FailureReason { get; private set; } = string.Empty;

    public DateTimeOffset FailedAt { get; private set; }

    internal static PublishFailedIssue FromInProgress(
        InProgressIssue source,
        string branchName,
        string failureReason,
        DateTimeOffset failedAt)
    {
        PublishFailedIssue publishFailed = new(source.Id);
        publishFailed.CopySharedPropertiesFrom(source);
        publishFailed.WorkerRunId = source.WorkerRunId;
        publishFailed.BranchName = branchName;
        publishFailed.FailureReason = failureReason;
        publishFailed.FailedAt = failedAt;
        return publishFailed;
    }

    internal static PublishFailedIssue FromRevisionInProgress(
        RevisionInProgressIssue source,
        string failureReason,
        DateTimeOffset failedAt)
    {
        PublishFailedIssue publishFailed = new(source.Id);
        publishFailed.CopySharedPropertiesFrom(source);
        publishFailed.WorkerRunId = source.WorkerRunId;
        publishFailed.BranchName = source.BranchName;
        publishFailed.PullRequestUrl = source.PullRequestUrl;
        publishFailed.Trigger = source.Trigger;
        publishFailed.ChecksFixAttempts = source.ChecksFixAttempts;
        publishFailed.RebaseConflictAttempts = source.RebaseConflictAttempts;
        publishFailed.FailureReason = failureReason;
        publishFailed.FailedAt = failedAt;
        return publishFailed;
    }

    /// <summary>
    /// An operator retried the publish: the same run publishes again, so the issue returns to the work-in-progress
    /// state it left.
    /// </summary>
    public Issue RetryPublish()
    {
        if (Trigger is { } trigger)
        {
            RevisionInProgressIssue revision = RevisionInProgressIssue.FromPublishFailed(this, trigger);
            AddDomainEvent(new Events.IssueRevisionInProgress(Id, MonitoredRepositoryId));
            return revision;
        }

        InProgressIssue inProgress = InProgressIssue.FromPublishFailed(this);
        AddDomainEvent(new Events.IssueInProgress(Id, MonitoredRepositoryId));
        return inProgress;
    }

    /// <summary>
    /// The run ended without publishing (its unpublished work was discarded). Retry Issue then resumes from whatever
    /// the branch holds.
    /// </summary>
    public Issue Fail(string failureReason, FailureCategoryVO failureCategory, DateTimeOffset failedAt)
    {
        if (Trigger is { } trigger)
        {
            RevisionFailedIssue revisionFailed = RevisionFailedIssue.FromPublishFailed(
                this,
                trigger,
                failureReason,
                failureCategory,
                failedAt);
            AddDomainEvent(new Events.IssueRevisionFailed(Id, MonitoredRepositoryId));
            return revisionFailed;
        }

        ContinuableFailedIssue failed = ContinuableFailedIssue.FromPublishFailed(
            this,
            failureReason,
            failureCategory,
            failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
