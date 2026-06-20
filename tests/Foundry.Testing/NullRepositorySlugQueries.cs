using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;

namespace Foundry.Testing;

public sealed class NullRepositorySlugQueries : IRepositorySlugQueries
{
    public Task<IReadOnlyDictionary<MonitoredRepositoryId, string>> GetSlugsAsync(
        IReadOnlySet<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<MonitoredRepositoryId, string>>(
            new Dictionary<MonitoredRepositoryId, string>());

    public Task<string?> GetProviderTypeAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);
}
