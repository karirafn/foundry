namespace Foundry.Modules.Monitoring.Contracts.Queries;

public interface IRepositoryEligibilityQuery
{
    Task<RepositoryEligibilityInfo?> GetEligibilityAsync(
        Guid repositoryId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EligibleRepository>> GetEligibleRepositoriesAsync(
        IReadOnlyCollection<Guid> repositoryIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> GetEligibilityStatusesAsync(
        IReadOnlyCollection<Guid> repositoryIds,
        CancellationToken cancellationToken);
}
