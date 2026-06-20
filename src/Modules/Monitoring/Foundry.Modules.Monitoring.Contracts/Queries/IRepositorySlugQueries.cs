namespace Foundry.Modules.Monitoring.Contracts.Queries;

public interface IRepositorySlugQueries
{
    Task<IReadOnlyDictionary<MonitoredRepositoryId, string>> GetSlugsAsync(
        IReadOnlySet<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken);

    Task<string?> GetProviderTypeAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);
}
