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

    Task<IReadOnlySet<int>> GetUntrackableIssueNumbersAsync(
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
