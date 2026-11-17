using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Testing;

/// <summary>
/// <see cref="IRepositoryDispatchQueries"/> stub that answers one fixed <see cref="RepositoryDispatchInfo"/> (or none)
/// and one fixed <see cref="WorkerNuGetConfig"/> (none unless set), and records each repository and token it was asked about.
/// </summary>
public sealed class StubRepositoryDispatchQueries(RepositoryDispatchInfo? info) : IRepositoryDispatchQueries
{
    private readonly List<MonitoredRepositoryId> _requestedRepositories = [];
    private readonly List<string> _requestedRunSourceTokens = [];

    public IReadOnlyList<MonitoredRepositoryId> RequestedRepositories => _requestedRepositories;

    /// <summary>The Run Source Tokens <see cref="GetWorkerNuGetConfigAsync"/> was called with.</summary>
    public IReadOnlyList<string> RequestedRunSourceTokens => _requestedRunSourceTokens;

    /// <summary>What <see cref="GetWorkerNuGetConfigAsync"/> answers; <see langword="null"/> (no Package Source) by default.</summary>
    public WorkerNuGetConfig? WorkerNuGetConfig { get; init; }

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

    public Task<WorkerNuGetConfig?> GetWorkerNuGetConfigAsync(
        MonitoredRepositoryId repositoryId,
        string runSourceToken,
        CancellationToken cancellationToken)
    {
        _requestedRunSourceTokens.Add(runSourceToken);
        return Task.FromResult(WorkerNuGetConfig);
    }
}
