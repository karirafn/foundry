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

    public InProgressIssue Claim(Guid workerRunId)
    {
        throw new NotImplementedException(
            "Claim() will be implemented in step 4 when RevisionInProgressIssue exists.");
    }
}
