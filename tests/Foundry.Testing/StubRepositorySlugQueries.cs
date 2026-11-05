using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;

namespace Foundry.Testing;

/// <summary>
/// In-memory <see cref="IRepositorySlugQueries"/> returning the display info registered via
/// <see cref="AddSlug"/>; unregistered ids are omitted, matching the real query.
/// </summary>
public sealed class StubRepositorySlugQueries : IRepositorySlugQueries
{
    private readonly Dictionary<MonitoredRepositoryId, RepositoryDisplayInfo> _displayInfos = [];

    public void AddSlug(MonitoredRepositoryId repositoryId, string slug, string? providerType = null)
    {
        _displayInfos[repositoryId] = new RepositoryDisplayInfo(slug, providerType);
    }

    public Task<IReadOnlyDictionary<MonitoredRepositoryId, RepositoryDisplayInfo>> GetRepositoryDisplayInfoAsync(
        IReadOnlySet<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken)
    {
        Dictionary<MonitoredRepositoryId, RepositoryDisplayInfo> result = repositoryIds
            .Where(_displayInfos.ContainsKey)
            .ToDictionary(id => id, id => _displayInfos[id]);

        return Task.FromResult<IReadOnlyDictionary<MonitoredRepositoryId, RepositoryDisplayInfo>>(result);
    }
}
