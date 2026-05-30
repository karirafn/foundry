using Foundry.Modules.Issues.Contracts;

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

    internal static ReviewIssue FromInProgress(
        InProgressIssue source,
        Guid workerRunId,
        string branchName,
        string pullRequestUrl)
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
        return review;
    }

    public QueuedIssue Retry()
    {
        QueuedIssue queued = QueuedIssue.FromRetry(this);
        AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    public CompletedIssue Complete(DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromReview(this, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }
}
