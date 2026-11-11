using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Workers.Contracts;

using FailureCategoryVO = Foundry.Modules.Workers.Contracts.FailureCategory;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class RevisionFailedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private RevisionFailedIssue()
    {
    }

    private RevisionFailedIssue(IssueId id) : base(id)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public override string? LinkedPullRequestUrl => NullIfEmpty(PullRequestUrl);

    // Placeholder replaced by the factory method or EF materialisation; invalid until assigned.
    public RevisionTrigger Trigger { get; private set; } = new RevisionTrigger.ReviewFeedback([], 0, null);

    public int ChecksFixAttempts { get; private set; }

    public int RebaseConflictAttempts { get; private set; }

    public string FailureReason { get; private set; } = string.Empty;

    public FailureCategoryVO FailureCategory { get; private set; } = FailureCategoryVO.NonZeroExit;

    public DateTimeOffset FailedAt { get; private set; }

    internal static RevisionFailedIssue FromRevisionInProgress(
        RevisionInProgressIssue source,
        string failureReason,
        FailureCategoryVO failureCategory,
        DateTimeOffset failedAt)
    {
        RevisionFailedIssue failed = new(source.Id);
        failed.CopySharedPropertiesFrom(source);
        failed.WorkerRunId = source.WorkerRunId;
        failed.BranchName = source.BranchName;
        failed.PullRequestUrl = source.PullRequestUrl;
        failed.Trigger = source.Trigger;
        failed.ChecksFixAttempts = source.ChecksFixAttempts;
        failed.RebaseConflictAttempts = source.RebaseConflictAttempts;
        failed.FailureReason = failureReason;
        failed.FailureCategory = failureCategory;
        failed.FailedAt = failedAt;
        return failed;
    }

    internal static RevisionFailedIssue FromPublishFailed(
        PublishFailedIssue source,
        RevisionTrigger trigger,
        string failureReason,
        FailureCategoryVO failureCategory,
        DateTimeOffset failedAt)
    {
        RevisionFailedIssue failed = new(source.Id);
        failed.CopySharedPropertiesFrom(source);
        failed.WorkerRunId = source.WorkerRunId;
        failed.BranchName = source.BranchName;
        failed.PullRequestUrl = source.PullRequestUrl;
        failed.Trigger = trigger;
        failed.ChecksFixAttempts = source.ChecksFixAttempts;
        failed.RebaseConflictAttempts = source.RebaseConflictAttempts;
        failed.FailureReason = failureReason;
        failed.FailureCategory = failureCategory;
        failed.FailedAt = failedAt;
        return failed;
    }

    public RevisionQueuedIssue Retry()
    {
        RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromRevisionFailed(this);
        AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
        return revisionQueued;
    }
}
