using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;

namespace Foundry.Testing;

/// <summary>
/// <see cref="IIssueContentQueries"/> stub that answers a configured result (by default an issue with no comments,
/// and a pull request with <see cref="DefaultPullRequestDescription"/>) and records each issue and pull request it
/// was asked about.
/// </summary>
public sealed class StubIssueContentQueries(
    Result<IssueContent>? result = null,
    Result<string>? pullRequestDescription = null) : IIssueContentQueries
{
    private readonly List<(MonitoredRepositoryId RepositoryId, int IssueNumber)> _requests = [];
    private readonly List<(MonitoredRepositoryId RepositoryId, string PullRequestUrl)> _pullRequestRequests = [];

    public static IssueContent DefaultContent { get; } = new("Stub issue", "Stub body.", [], SpecHashFixtures.Any);

    public const string DefaultPullRequestDescription = "Stub pull request description.";

    public IReadOnlyList<(MonitoredRepositoryId RepositoryId, int IssueNumber)> Requests => _requests;

    public IReadOnlyList<(MonitoredRepositoryId RepositoryId, string PullRequestUrl)> PullRequestRequests =>
        _pullRequestRequests;

    public Task<Result<IssueContent>> GetIssueContentAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        _requests.Add((repositoryId, issueNumber));
        return Task.FromResult(result ?? Result<IssueContent>.Ok(DefaultContent));
    }

    public Task<Result<string>> GetPullRequestDescriptionAsync(
        MonitoredRepositoryId repositoryId,
        string pullRequestUrl,
        CancellationToken cancellationToken)
    {
        _pullRequestRequests.Add((repositoryId, pullRequestUrl));
        return Task.FromResult(pullRequestDescription ?? Result<string>.Ok(DefaultPullRequestDescription));
    }
}
