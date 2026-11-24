using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

public interface IIssueQueries
{
    Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
        MonitoredRepositoryId repositoryId,
        IReadOnlySet<int> issueNumbers,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReviewIssueInfo>> GetReviewIssuesAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The repository's issues in the review step (ADR 0113): queued for, running, or waiting to retry a review. The
    /// review pass reads only whether their issue or pull request closed.
    /// </summary>
    Task<IReadOnlyList<ReviewStepIssueInfo>> GetReviewStepIssuesAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The repository's revisions that wait to start, queued or held by open blockers. The review pass reads only
    /// whether their pull request closed.
    /// </summary>
    Task<IReadOnlyList<QueuedRevisionIssueInfo>> GetQueuedRevisionIssuesAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChecksPendingIssueInfo>> GetChecksPendingIssuesAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RebasePendingIssueInfo>> GetRebasePendingIssuesAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MergePendingIssueInfo>> GetMergePendingIssuesAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The repository's issues the catch-up queue reads (ADR 0111): every checks-pending and merge-pending issue, and
    /// every rebase-pending issue rebasing for a catch-up.
    /// </summary>
    Task<IReadOnlyList<MergeQueueIssueInfo>> GetMergeQueueIssuesAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AwaitingInputIssueInfo>> GetAwaitingInputIssuesAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    Task<IReadOnlySet<int>> GetUntrackableIssueNumbersAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The numbers of the repository's issues whose run holds unpublished work (<c>publish_blocked</c> and
    /// <c>publish_failed</c>). The listing pass asks the provider whether one is closed when it is missing from a
    /// complete listing.
    /// </summary>
    Task<IReadOnlySet<int>> GetUnpublishedRunIssueNumbersAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    Task<IReadOnlySet<int>> GetDispatchCandidateIssueNumbersAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IssueSummary>> GetIssueSummariesAsync(
        MonitoredRepositoryId? repositoryId,
        CancellationToken cancellationToken);

    Task<IssueSummary?> GetIssueSummaryAsync(IssueId issueId, CancellationToken cancellationToken);

    Task<Result<IssueDetail>> GetIssueDetailAsync(IssueId issueId, CancellationToken cancellationToken);

    Task<IReadOnlyList<IssueSummary>> GetActiveIssueSummariesAsync(
        MonitoredRepositoryId? repositoryId,
        IReadOnlyCollection<string>? states,
        CancellationToken cancellationToken);

    Task<PagedIssues> GetResolvedIssueSummariesAsync(
        MonitoredRepositoryId? repositoryId,
        IReadOnlyCollection<string> states,
        string? cursor,
        int limit,
        CancellationToken cancellationToken);

    Task<IssueStateCounts> GetIssueStateCountsAsync(
        MonitoredRepositoryId? repositoryId,
        CancellationToken cancellationToken);
}
