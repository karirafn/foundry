using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Issues.Domain;

public sealed class ReviewIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private ReviewIssue()
    {
    }

    private ReviewIssue(IssueId id) : base(id)
    {
    }

    public Guid WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public DateTimeOffset FeedbackCutoffAt { get; private set; }

    internal static ReviewIssue FromInProgress(
        InProgressIssue source,
        Guid workerRunId,
        string branchName,
        string pullRequestUrl,
        DateTimeOffset feedbackCutoffAt)
    {
        ReviewIssue review = new(source.Id);
        review.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        review.WorkerRunId = workerRunId;
        review.BranchName = branchName;
        review.PullRequestUrl = pullRequestUrl;
        review.FeedbackCutoffAt = feedbackCutoffAt;
        return review;
    }

    internal static ReviewIssue FromRevisionInProgress(
        RevisionInProgressIssue source,
        DateTimeOffset feedbackCutoffAt)
    {
        ReviewIssue review = new(source.Id);
        review.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        review.WorkerRunId = source.WorkerRunId;
        review.BranchName = source.BranchName;
        review.PullRequestUrl = source.PullRequestUrl;
        review.FeedbackCutoffAt = feedbackCutoffAt;
        return review;
    }

    public RevisionQueuedIssue Revise(IReadOnlyList<ReviewComment> comments)
    {
        RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromReview(this, comments);
        AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
        return revisionQueued;
    }

    public ContinuationQueuedIssue Retry()
    {
        ContinuationQueuedIssue queued = ContinuationQueuedIssue.FromReview(this);
        AddDomainEvent(new Events.IssueContinuationQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    public CompletedIssue Complete(DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromReview(this, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }

    public ContinuableFailedIssue Fail(string failureReason, string failureCategory, DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromReview(
            this,
            failureReason,
            failureCategory,
            failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
