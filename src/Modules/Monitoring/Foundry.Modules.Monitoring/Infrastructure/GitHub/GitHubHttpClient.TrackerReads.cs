using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.TrackerReads;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal sealed partial class GitHubHttpClient
{
    private const string BlockedBySegment = "blocked_by";
    private const string BlockingSegment = "blocking";
    private const int DependenciesPerPage = 100;

    private const string OpenIssueIndexQuery = """
        query OpenIssueIndex($owner: String!, $name: String!) {
          rateLimit { cost remaining limit resetAt }
          repository(owner: $owner, name: $name) {
            issues(first: 100, states: OPEN, orderBy: {field: CREATED_AT, direction: DESC}) {
              nodes { number title }
            }
          }
        }
        """;

    /// <summary>
    /// Reads the issues <paramref name="issueNumber"/> is blocked by, then the issues it blocks
    /// (<c>GET /repos/{owner}/{repo}/issues/{issue_number}/dependencies/blocked_by</c> and <c>.../blocking</c>),
    /// of any state.
    /// </summary>
    public async Task<Result<IReadOnlyList<IssueReference>>> GetIssueLinksAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueNumber,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<IssueReference>>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        Result<IReadOnlyList<IssueReference>> blockedBy = await ReadDependenciesAsync(
            apiBaseUrl, slug, issueNumber, BlockedBySegment, IssueRelation.BlockedBy, token, cancellationToken);

        if (blockedBy is not Result<IReadOnlyList<IssueReference>>.Success blockedBySuccess)
        {
            return blockedBy;
        }

        Result<IReadOnlyList<IssueReference>> blocking = await ReadDependenciesAsync(
            apiBaseUrl, slug, issueNumber, BlockingSegment, IssueRelation.Blocks, token, cancellationToken);

        if (blocking is not Result<IReadOnlyList<IssueReference>>.Success blockingSuccess)
        {
            return blocking;
        }

        return Result<IReadOnlyList<IssueReference>>.Ok([.. blockedBySuccess.Value, .. blockingSuccess.Value]);
    }

    /// <summary>
    /// Reads the repository's open issues, newest first, at most 100. GraphQL's <c>Repository.issues</c>
    /// returns issues only, so a pull request never appears.
    /// </summary>
    public async Task<Result<IReadOnlyList<OpenIssue>>> GetOpenIssuesAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        object variables = new { owner = slug.Owner, name = slug.Name };
        Result<OpenIssueIndexData> result = await ExecuteGraphQlAsync<OpenIssueIndexData>(
            apiBaseUrl, OpenIssueIndexQuery, variables, token, cancellationToken);

        if (result is not Result<OpenIssueIndexData>.Success success)
        {
            return Result<IReadOnlyList<OpenIssue>>.Fail(((Result<OpenIssueIndexData>.Failure)result).Error);
        }

        IReadOnlyList<OpenIssue> issues = (success.Value.Repository?.Issues?.Nodes ?? [])
            .Select(node => new OpenIssue(node.Number, node.Title))
            .ToList();

        return Result<IReadOnlyList<OpenIssue>>.Ok(issues);
    }

    private async Task<Result<IReadOnlyList<IssueReference>>> ReadDependenciesAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueNumber,
        string segment,
        IssueRelation relation,
        string token,
        CancellationToken cancellationToken)
    {
        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string relativePath = $"repos/{owner}/{repo}/issues/{issueNumber}/dependencies/{segment}?per_page={DependenciesPerPage}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddGitHubHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<IssueReference>>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitHubDependencyDto> dtos = JsonSerializer.Deserialize<List<GitHubDependencyDto>>(body, JsonOptions) ?? [];

        string expectedFullName = $"{slug.Owner}/{slug.Name}";

        IReadOnlyList<IssueReference> references = dtos
            .Where(dto => string.Equals(dto.Repository.FullName, expectedFullName, StringComparison.OrdinalIgnoreCase))
            .Select(dto => new IssueReference(dto.Number, relation))
            .ToList();

        return Result<IReadOnlyList<IssueReference>>.Ok(references);
    }

    private interface IReportsRateLimit
    {
        GraphQlRateLimit? RateLimit { get; }
    }

    private sealed record OpenIssueIndexData(GraphQlRateLimit? RateLimit, OpenIssueIndexRepository? Repository)
        : IReportsRateLimit;

    private sealed record OpenIssueIndexRepository(OpenIssueIndexConnection? Issues);

    private sealed record OpenIssueIndexConnection(IReadOnlyList<OpenIssueIndexNode> Nodes);

    private sealed record OpenIssueIndexNode(int Number, string Title);
}
