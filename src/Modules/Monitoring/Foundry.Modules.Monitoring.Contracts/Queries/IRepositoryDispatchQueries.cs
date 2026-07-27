namespace Foundry.Modules.Monitoring.Contracts;

public interface IRepositoryDispatchQueries
{
    Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);
}
