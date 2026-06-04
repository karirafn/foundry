using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Issues.Domain;

public sealed class RevisionQueuedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private RevisionQueuedIssue()
    {
    }

    private RevisionQueuedIssue(IssueId id) : base(id)
    {
    }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public IReadOnlyList<ReviewComment> ReviewComments { get; private set; } = [];

    internal static RevisionQueuedIssue FromReview(
        ReviewIssue source,
        IReadOnlyList<ReviewComment> comments)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id);
        revisionQueued.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.ReviewComments = comments;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromRevisionFailed(RevisionFailedIssue source)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id);
        revisionQueued.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.ReviewComments = source.ReviewComments;
        return revisionQueued;
    }

    public RevisionInProgressIssue Claim(Guid workerRunId)
    {
        RevisionInProgressIssue revisionInProgress = RevisionInProgressIssue.FromRevisionQueued(this, workerRunId);
        AddDomainEvent(new Events.IssueRevisionInProgress(Id, MonitoredRepositoryId));
        return revisionInProgress;
    }
}
