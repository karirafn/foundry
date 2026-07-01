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

    public ReviewIssue MarkInReview(
        Guid workerRunId,
        string branchName,
        string pullRequestUrl,
        DateTimeOffset feedbackCutoffAt)
    {
        ReviewIssue review = ReviewIssue.FromInProgress(
            this,
            workerRunId,
            branchName,
            pullRequestUrl,
            feedbackCutoffAt);
        AddDomainEvent(new Events.IssueInReview(Id, MonitoredRepositoryId));
        return review;
    }

    public UnchangedIssue MarkUnchanged(Guid workerRunId)
    {
        UnchangedIssue unchanged = UnchangedIssue.FromInProgress(this, workerRunId);
        AddDomainEvent(new Events.IssueUnchanged(Id, MonitoredRepositoryId));
        return unchanged;
    }

    public CompletedIssue MarkCompleted(
        Guid workerRunId,
        string branchName,
        string pullRequestUrl,
        DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromInProgress(this, branchName, pullRequestUrl, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }

    public FailedIssue MarkFailed(
        Guid workerRunId,
        string failureReason,
        DateTimeOffset failedAt,
        string failureCategory)
    {
        FailedIssue failed = FailedIssue.FromInProgress(this, workerRunId, failureReason, failureCategory, failedAt);
        AddDomainEvent(new Events.IssueFailed(Id, MonitoredRepositoryId));
        return failed;
    }

    public ContinuableFailedIssue MarkContinuableFailed(
        Guid workerRunId,
        string branchName,
        string failureReason,
        string failureCategory,
        DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromInProgress(
            this,
            workerRunId,
            branchName,
            failureReason,
            failureCategory,
            failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }

    internal static InProgressIssue FromContinuationQueued(ContinuationQueuedIssue source, Guid workerRunId)
    {
        InProgressIssue inProgress = new(source.Id);
        inProgress.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        inProgress.WorkerRunId = workerRunId;
        return inProgress;
    }
}
