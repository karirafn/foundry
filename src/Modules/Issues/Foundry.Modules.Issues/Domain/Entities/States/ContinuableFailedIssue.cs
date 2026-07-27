using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class ContinuableFailedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private ContinuableFailedIssue()
    {
    }

    private ContinuableFailedIssue(IssueId id) : base(id)
    {
    }

    public Guid WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public string FailureReason { get; private set; } = string.Empty;

    public string FailureCategory { get; private set; } = string.Empty;

    public DateTimeOffset FailedAt { get; private set; }

    internal static ContinuableFailedIssue FromInProgress(
        InProgressIssue source,
        Guid workerRunId,
        string branchName,
        string failureReason,
        string failureCategory,
        DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = new(source.Id);
        failed.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        failed.WorkerRunId = workerRunId;
        failed.BranchName = branchName;
        failed.FailureReason = failureReason;
        failed.FailureCategory = failureCategory;
        failed.FailedAt = failedAt;
        return failed;
    }

    internal static ContinuableFailedIssue FromReview(
        ReviewIssue source,
        string failureReason,
        string failureCategory,
        DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = new(source.Id);
        failed.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        failed.WorkerRunId = source.WorkerRunId;
        failed.BranchName = source.BranchName;
        failed.PullRequestUrl = source.PullRequestUrl;
        failed.FailureReason = failureReason;
        failed.FailureCategory = failureCategory;
        failed.FailedAt = failedAt;
        return failed;
    }

    public ContinuationQueuedIssue Retry()
    {
        ContinuationQueuedIssue queued = ContinuationQueuedIssue.FromContinuableFailed(this);
        AddDomainEvent(new Events.IssueContinuationQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
