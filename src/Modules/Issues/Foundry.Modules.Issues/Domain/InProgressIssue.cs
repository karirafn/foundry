using Foundry.Modules.Issues.Contracts;

namespace Foundry.Modules.Issues.Domain;

public sealed class InProgressIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private InProgressIssue()
    {
    }

    private InProgressIssue(IssueId id) : base(id)
    {
    }

    public Guid WorkerRunId { get; private set; }

    internal static InProgressIssue FromQueued(QueuedIssue queued, Guid workerRunId)
    {
        InProgressIssue inProgress = new(queued.Id);
        inProgress.SetSharedProperties(
            queued.MonitoredRepositoryId,
            queued.IssueNumber,
            queued.Title,
            queued.Body,
            queued.Author,
            queued.Url,
            queued.Labels,
            queued.DetectedAt);
        inProgress.WorkerRunId = workerRunId;
        return inProgress;
    }

    public ReviewIssue MarkInReview(Guid workerRunId, string branchName, string pullRequestUrl)
    {
        ReviewIssue review = ReviewIssue.FromInProgress(this, workerRunId, branchName, pullRequestUrl);
        AddDomainEvent(new Events.IssueInReview(Id, MonitoredRepositoryId));
        return review;
    }

    public UnchangedIssue MarkUnchanged(Guid workerRunId)
    {
        UnchangedIssue unchanged = UnchangedIssue.FromInProgress(this, workerRunId);
        AddDomainEvent(new Events.IssueUnchanged(Id, MonitoredRepositoryId));
        return unchanged;
    }

    public FailedIssue MarkFailed(Guid workerRunId, string failureReason, DateTimeOffset failedAt)
    {
        FailedIssue failed = FailedIssue.FromInProgress(this, workerRunId, failureReason, failedAt);
        AddDomainEvent(new Events.IssueFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
