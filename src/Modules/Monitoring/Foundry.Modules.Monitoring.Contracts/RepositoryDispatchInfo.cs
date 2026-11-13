namespace Foundry.Modules.Monitoring.Contracts;

/// <param name="Host">The repository's provider host (<c>MonitoredRepository.Host</c>), which a host pause holds.</param>
public sealed record RepositoryDispatchInfo(
    string RepositorySlug,
    Uri CloneUrl,
    string? AccountToken,
    WorkerProvider Provider,
    string IssueApiUrlBase,
    string Host);
