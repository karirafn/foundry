namespace Foundry.Modules.Monitoring.Contracts.Queries;

public interface IRepositorySlugQueries
{
    /// <summary>
    /// Returns each repository's slug and the provider type (<c>"github"</c> / <c>"gitlab"</c>) of the
    /// credential covering it, or a null provider type when no credential covers the repository.
    /// Ids that match no repository are omitted.
    /// </summary>
    Task<IReadOnlyDictionary<MonitoredRepositoryId, RepositoryDisplayInfo>> GetRepositoryDisplayInfoAsync(
        IReadOnlySet<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken);
}
