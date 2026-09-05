using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class InProgressIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private InProgressIssue()
    {
    }

    private InProgressIssue(IssueId id) : base(id)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    internal static InProgressIssue FromQueued(FreshQueuedIssue queued, WorkerRunId workerRunId)
    {
        InProgressIssue inProgress = new(queued.Id);
        inProgress.SetSharedProperties(
            queued.MonitoredRepositoryId,
            queued.IssueNumber,
            queued.Title,
            queued.Author,
            queued.Url,
            queued.Labels,
            queued.DetectedAt);
        inProgress.WorkerRunId = workerRunId;
        return inProgress;
    }

    public ReviewIssue MarkInReview(
        string branchName,
        string pullRequestUrl,
        DateTimeOffset feedbackCutoffAt)
    {
        ReviewIssue review = ReviewIssue.FromInProgress(this, branchName, pullRequestUrl, feedbackCutoffAt);
        AddDomainEvent(new Events.IssueInReview(Id, MonitoredRepositoryId));
        return review;
    }

    public UnchangedIssue MarkUnchanged()
    {
        UnchangedIssue unchanged = UnchangedIssue.FromInProgress(this);
        AddDomainEvent(new Events.IssueUnchanged(Id, MonitoredRepositoryId));
        return unchanged;
    }

    public CompletedIssue MarkCompleted(
        string branchName,
        string pullRequestUrl,
        DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromInProgress(this, branchName, pullRequestUrl, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }

    public FailedIssue MarkFailed(
        string failureReason,
        DateTimeOffset failedAt,
        FailureCategory failureCategory)
    {
        FailedIssue failed = FailedIssue.FromInProgress(this, failureReason, failureCategory, failedAt);
        AddDomainEvent(new Events.IssueFailed(Id, MonitoredRepositoryId));
        return failed;
    }

    public ContinuableFailedIssue MarkContinuableFailed(
        string branchName,
        string failureReason,
        FailureCategory failureCategory,
        DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromInProgress(
            this,
            branchName,
            failureReason,
            failureCategory,
            failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }

    internal static InProgressIssue FromContinuationQueued(ContinuationQueuedIssue source, WorkerRunId workerRunId)
    {
        InProgressIssue inProgress = new(source.Id);
        inProgress.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        inProgress.WorkerRunId = workerRunId;
        return inProgress;
    }
}
