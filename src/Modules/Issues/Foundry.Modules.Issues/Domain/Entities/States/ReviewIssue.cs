using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class ReviewIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private ReviewIssue()
    {
    }

    private ReviewIssue(IssueId id) : base(id)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public DateTimeOffset FeedbackCutoffAt { get; private set; }

    internal static ReviewIssue FromInProgress(
        InProgressIssue source,
        string branchName,
        string pullRequestUrl,
        DateTimeOffset feedbackCutoffAt)
    {
        ReviewIssue review = new(source.Id);
        review.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        review.WorkerRunId = source.WorkerRunId;
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

    public RevisionQueuedIssue Revise(
        IReadOnlyList<ReviewComment> comments,
        int omittedCommentCount = 0,
        DateTimeOffset? newestCommentAt = null)
    {
        RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromReview(
            this,
            comments,
            omittedCommentCount,
            newestCommentAt);
        AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
        return revisionQueued;
    }

    public CompletedIssue Complete(DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromReview(this, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }

    public ContinuableFailedIssue Fail(string failureReason, FailureCategory failureCategory, DateTimeOffset failedAt)
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
