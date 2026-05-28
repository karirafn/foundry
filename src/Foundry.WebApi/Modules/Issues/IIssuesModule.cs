using Foundry.WebApi.Modules.Monitoring.Domain;

namespace Foundry.WebApi.Modules.Issues;

public interface IIssuesModule
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

    Task<ClaimedIssueDispatch?> ClaimNextQueuedIssueAsync(Guid workerRunId, CancellationToken cancellationToken);
}
