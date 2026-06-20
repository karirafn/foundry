namespace Foundry.Modules.Monitoring.Contracts.Queries;

public interface IRepositoryEligibilityQuery
{
    Task<RepositoryEligibilityInfo?> GetEligibilityAsync(
        Guid repositoryId,
        CancellationToken cancellationToken);

    Task<IReadOnlySet<Guid>> GetEligibleRepositoryIdsAsync(
        IReadOnlyCollection<Guid> repositoryIds,
        CancellationToken cancellationToken);
}
