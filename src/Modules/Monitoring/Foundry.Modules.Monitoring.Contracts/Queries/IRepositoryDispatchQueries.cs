namespace Foundry.Modules.Monitoring.Contracts.Queries;

public interface IRepositoryDispatchQueries
{
    Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);
}
