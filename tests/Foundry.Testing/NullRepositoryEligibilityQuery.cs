using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;

namespace Foundry.Testing;

public sealed class NullRepositoryEligibilityQuery : IRepositoryEligibilityQuery
{
    public Task<RepositoryEligibilityInfo?> GetEligibilityAsync(
        Guid repositoryId,
        CancellationToken cancellationToken)
        => Task.FromResult<RepositoryEligibilityInfo?>(null);

    public Task<IReadOnlyList<EligibleRepository>> GetEligibleRepositoriesAsync(
        IReadOnlyCollection<Guid> repositoryIds,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<EligibleRepository>>([]);

    public Task<IReadOnlyDictionary<Guid, string>> GetEligibilityStatusesAsync(
        IReadOnlyCollection<Guid> repositoryIds,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}
