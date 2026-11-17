namespace Foundry.Modules.Monitoring.Contracts;

public interface IRepositoryDispatchQueries
{
    Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The <c>nuget.config</c> a run of <paramref name="repositoryId"/> restores with, carrying
    /// <paramref name="runSourceToken"/>; <see langword="null"/> when the repository declares no Package Source the
    /// proxy serves, so the worker restores with the repository's own config.
    /// </summary>
    Task<WorkerNuGetConfig?> GetWorkerNuGetConfigAsync(
        MonitoredRepositoryId repositoryId,
        string runSourceToken,
        CancellationToken cancellationToken);
}
