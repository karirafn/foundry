using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;

namespace Foundry.Testing;

/// <summary>Finds no repository, so every issue's path reads the default repository settings.</summary>
public sealed class NullRepositoryLifecycleSettingsQueries : IRepositoryLifecycleSettingsQueries
{
    public Task<IReadOnlyDictionary<MonitoredRepositoryId, RepositoryLifecycleSettings>> GetLifecycleSettingsAsync(
        IReadOnlySet<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<MonitoredRepositoryId, RepositoryLifecycleSettings>>(
            new Dictionary<MonitoredRepositoryId, RepositoryLifecycleSettings>());
}
