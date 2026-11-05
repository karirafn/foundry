using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;

namespace Foundry.Testing;

public sealed class NullRepositorySlugQueries : IRepositorySlugQueries
{
    public Task<IReadOnlyDictionary<MonitoredRepositoryId, RepositoryDisplayInfo>> GetRepositoryDisplayInfoAsync(
        IReadOnlySet<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<MonitoredRepositoryId, RepositoryDisplayInfo>>(
            new Dictionary<MonitoredRepositoryId, RepositoryDisplayInfo>());
}
