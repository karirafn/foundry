using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Issues.Domain;

public sealed class RevisionFailedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private RevisionFailedIssue()
    {
    }

    private RevisionFailedIssue(IssueId id) : base(id)
    {
    }

    public Guid WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public IReadOnlyList<ReviewComment> ReviewComments { get; private set; } = [];

    public string FailureReason { get; private set; } = string.Empty;

    public DateTimeOffset FailedAt { get; private set; }

    internal static RevisionFailedIssue FromRevisionInProgress(
        RevisionInProgressIssue source,
        string failureReason,
        DateTimeOffset failedAt)
    {
        RevisionFailedIssue failed = new(source.Id);
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
        failed.ReviewComments = source.ReviewComments;
        failed.FailureReason = failureReason;
        failed.FailedAt = failedAt;
        return failed;
    }

    public RevisionQueuedIssue Retry()
    {
        RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromRevisionFailed(this);
        AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
        return revisionQueued;
    }
}
