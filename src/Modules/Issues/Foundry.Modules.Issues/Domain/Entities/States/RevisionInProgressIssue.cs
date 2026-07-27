using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class RevisionInProgressIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private RevisionInProgressIssue()
    {
    }

    private RevisionInProgressIssue(IssueId id) : base(id)
    {
    }

    public Guid WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public IReadOnlyList<ReviewComment> ReviewComments { get; private set; } = [];

    internal static RevisionInProgressIssue FromRevisionQueued(RevisionQueuedIssue source, Guid workerRunId)
    {
        RevisionInProgressIssue revisionInProgress = new(source.Id);
        revisionInProgress.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        revisionInProgress.WorkerRunId = workerRunId;
        revisionInProgress.BranchName = source.BranchName;
        revisionInProgress.PullRequestUrl = source.PullRequestUrl;
        revisionInProgress.ReviewComments = source.ReviewComments;
        return revisionInProgress;
    }

    public ReviewIssue MarkInReview(DateTimeOffset feedbackCutoffAt)
    {
        ReviewIssue review = ReviewIssue.FromRevisionInProgress(this, feedbackCutoffAt);
        AddDomainEvent(new Events.IssueInReview(Id, MonitoredRepositoryId));
        return review;
    }

    public ReviewIssue MarkUnchanged(DateTimeOffset feedbackCutoffAt)
    {
        ReviewIssue review = ReviewIssue.FromRevisionInProgress(this, feedbackCutoffAt);
        AddDomainEvent(new Events.IssueInReview(Id, MonitoredRepositoryId));
        return review;
    }

    public RevisionFailedIssue MarkFailed(
        Guid workerRunId,
        string failureReason,
        string failureCategory,
        DateTimeOffset failedAt)
    {
        RevisionFailedIssue failed = RevisionFailedIssue.FromRevisionInProgress(
            this,
            failureReason,
            failureCategory,
            failedAt);
        AddDomainEvent(new Events.IssueRevisionFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
