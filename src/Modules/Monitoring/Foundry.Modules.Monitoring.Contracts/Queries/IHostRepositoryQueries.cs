namespace Foundry.Modules.Monitoring.Contracts.Queries;

public interface IHostRepositoryQueries
{
    /// <summary>Every monitored repository on <paramref name="host"/>, matched case-insensitively.</summary>
    Task<IReadOnlyList<MonitoredRepositoryId>> GetRepositoryIdsOnHostAsync(
        string host,
        CancellationToken cancellationToken);
}
