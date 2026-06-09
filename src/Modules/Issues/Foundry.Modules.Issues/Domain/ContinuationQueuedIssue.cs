using Foundry.Modules.Issues.Contracts;

namespace Foundry.Modules.Issues.Domain;

public sealed class ContinuationQueuedIssue : Issue
{
    private const string DefaultProgressFromReview = "PR was opened and reviewed";

    // Private parameterless constructor for EF Core materialization.
    private ContinuationQueuedIssue()
    {
    }

    private ContinuationQueuedIssue(IssueId id) : base(id)
    {
    }

    public string BranchName { get; private set; } = string.Empty;

    public string LatestProgress { get; private set; } = string.Empty;

    internal static ContinuationQueuedIssue FromContinuableFailed(ContinuableFailedIssue source)
    {
        ContinuationQueuedIssue continuationQueued = new(source.Id);
        continuationQueued.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        continuationQueued.BranchName = source.BranchName;
        continuationQueued.LatestProgress = source.LatestProgress;
        return continuationQueued;
    }

    internal static ContinuationQueuedIssue FromReview(ReviewIssue source)
    {
        ContinuationQueuedIssue continuationQueued = new(source.Id);
        continuationQueued.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        continuationQueued.BranchName = source.BranchName;
        continuationQueued.LatestProgress = DefaultProgressFromReview;
        return continuationQueued;
    }

    public InProgressIssue Claim(Guid workerRunId)
    {
        InProgressIssue inProgress = InProgressIssue.FromContinuationQueued(this, workerRunId);
        AddDomainEvent(new Events.IssueInProgress(Id, MonitoredRepositoryId));
        return inProgress;
    }
}
