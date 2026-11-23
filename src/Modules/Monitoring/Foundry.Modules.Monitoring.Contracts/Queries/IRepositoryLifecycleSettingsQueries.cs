namespace Foundry.Modules.Monitoring.Contracts.Queries;

/// <summary>Reads the repository settings that decide which steps an issue's lifecycle path takes.</summary>
public interface IRepositoryLifecycleSettingsQueries
{
    /// <summary>
    /// Returns the lifecycle settings of each of <paramref name="repositoryIds"/> in one read, removed repositories
    /// included. Ids that match no repository are omitted.
    /// </summary>
    Task<IReadOnlyDictionary<MonitoredRepositoryId, RepositoryLifecycleSettings>> GetLifecycleSettingsAsync(
        IReadOnlySet<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken);
}
