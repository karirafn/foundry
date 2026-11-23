using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;

namespace Foundry.Testing;

/// <summary>
/// In-memory <see cref="IRepositoryLifecycleSettingsQueries"/> returning the settings registered in
/// <see cref="Settings"/>, and recording every read.
/// </summary>
public sealed class StubRepositoryLifecycleSettingsQueries : IRepositoryLifecycleSettingsQueries
{
    private readonly List<IReadOnlySet<MonitoredRepositoryId>> _reads = [];

    public Dictionary<MonitoredRepositoryId, RepositoryLifecycleSettings> Settings { get; } = [];

    public IReadOnlyList<IReadOnlySet<MonitoredRepositoryId>> Reads => _reads;

    public Task<IReadOnlyDictionary<MonitoredRepositoryId, RepositoryLifecycleSettings>> GetLifecycleSettingsAsync(
        IReadOnlySet<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken)
    {
        _reads.Add(repositoryIds);
        Dictionary<MonitoredRepositoryId, RepositoryLifecycleSettings> result = repositoryIds
            .Where(Settings.ContainsKey)
            .ToDictionary(id => id, id => Settings[id]);

        return Task.FromResult<IReadOnlyDictionary<MonitoredRepositoryId, RepositoryLifecycleSettings>>(result);
    }
}
