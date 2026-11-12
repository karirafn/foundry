using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;

namespace Foundry.Testing;

/// <summary>
/// <see cref="ITrackerContextQueries"/> stub that answers a configured context (by default one that renders
/// nothing) and records each request.
/// </summary>
public sealed class StubTrackerContextQueries(TrackerContext? context = null) : ITrackerContextQueries
{
    private readonly List<(MonitoredRepositoryId RepositoryId, int IssueNumber, IssueContent IssueContent)> _requests = [];

    public static TrackerContext DefaultContext { get; } = new([], [], false, []);

    public IReadOnlyList<(MonitoredRepositoryId RepositoryId, int IssueNumber, IssueContent IssueContent)> Requests => _requests;

    public Task<TrackerContext> GetTrackerContextAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        IssueContent issueContent,
        CancellationToken cancellationToken)
    {
        _requests.Add((repositoryId, issueNumber, issueContent));
        return Task.FromResult(context ?? DefaultContext);
    }
}
