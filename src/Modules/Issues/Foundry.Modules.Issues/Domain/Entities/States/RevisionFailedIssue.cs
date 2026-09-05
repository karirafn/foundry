using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Monitoring.Contracts;
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

    public IReadOnlyList<ReviewComment> ReviewComments { get; private set; } = [];

    public int OmittedCommentCount { get; private set; }

    public DateTimeOffset? NewestConsumedCommentAt { get; private set; }

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
        failed.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        failed.WorkerRunId = source.WorkerRunId;
        failed.BranchName = source.BranchName;
        failed.PullRequestUrl = source.PullRequestUrl;
        failed.ReviewComments = source.ReviewComments;
        failed.OmittedCommentCount = source.OmittedCommentCount;
        failed.NewestConsumedCommentAt = source.NewestConsumedCommentAt;
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
