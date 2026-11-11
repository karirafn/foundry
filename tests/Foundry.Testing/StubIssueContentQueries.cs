using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;

namespace Foundry.Testing;

/// <summary>
/// <see cref="IIssueContentQueries"/> stub that answers a configured result (by default an issue with no comments)
/// and records each issue it was asked about.
/// </summary>
public sealed class StubIssueContentQueries(Result<IssueContent>? result = null) : IIssueContentQueries
{
    private readonly List<(MonitoredRepositoryId RepositoryId, int IssueNumber)> _requests = [];

    public static IssueContent DefaultContent { get; } = new("Stub issue", "Stub body.", []);

    public IReadOnlyList<(MonitoredRepositoryId RepositoryId, int IssueNumber)> Requests => _requests;

    public Task<Result<IssueContent>> GetIssueContentAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        _requests.Add((repositoryId, issueNumber));
        return Task.FromResult(result ?? Result<IssueContent>.Ok(DefaultContent));
    }
}
