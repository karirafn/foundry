using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Testing;

/// <summary>
/// <see cref="IRepositoryDispatchQueries"/> stub that answers one fixed <see cref="RepositoryDispatchInfo"/> (or none)
/// and records each repository it was asked about.
/// </summary>
public sealed class StubRepositoryDispatchQueries(RepositoryDispatchInfo? info) : IRepositoryDispatchQueries
{
    private readonly List<MonitoredRepositoryId> _requestedRepositories = [];

    public IReadOnlyList<MonitoredRepositoryId> RequestedRepositories => _requestedRepositories;

    public static StubRepositoryDispatchQueries WithToken(string? accountToken) => new(new RepositoryDispatchInfo(
        "owner/repo",
        new Uri("https://github.com/owner/repo.git"),
        accountToken,
        new WorkerProvider.GitHub(),
        "https://api.github.com/repos/owner/repo/issues",
        "github.com"));

    public Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
    {
        _requestedRepositories.Add(repositoryId);
        return Task.FromResult(info);
    }
}
