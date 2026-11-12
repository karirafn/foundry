using System.Text.Json;
using System.Text.Json.Serialization;

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
    private const string IssueTypeName = "Issue";

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

    private const string ReferencedIssueQuery = """
        query ReferencedIssue($owner: String!, $name: String!, $number: Int!) {
          rateLimit { cost remaining limit resetAt }
          repository(owner: $owner, name: $name) {
            issueOrPullRequest(number: $number) {
              __typename
              ... on Issue {
                title
                state
                body
                labels(first: 100) { nodes { name } }
                closedByPullRequestsReferences(first: 25, includeClosedPrs: true) { nodes { state } }
              }
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

    /// <summary>
    /// Reads one issue and the state of the pull requests that close it in a single GraphQL query. GitHub
    /// numbers issues and pull requests from one sequence, so a number naming a pull request fails with
    /// <see cref="TrackerReadErrors.NotAnIssueCode"/>.
    /// </summary>
    public async Task<Result<ProviderReferencedIssue>> GetReferencedIssueAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueNumber,
        string token,
        CancellationToken cancellationToken)
    {
        object variables = new { owner = slug.Owner, name = slug.Name, number = issueNumber };
        Result<ReferencedIssueData> result = await ExecuteGraphQlAsync<ReferencedIssueData>(
            apiBaseUrl, ReferencedIssueQuery, variables, token, cancellationToken);

        if (result is not Result<ReferencedIssueData>.Success success)
        {
            return Result<ProviderReferencedIssue>.Fail(((Result<ReferencedIssueData>.Failure)result).Error);
        }

        ReferencedIssueNode? node = success.Value.Repository?.IssueOrPullRequest;

        if (node is null)
        {
            return Result<ProviderReferencedIssue>.Fail(
                GitHubErrors.ProviderError("GraphQL response named no issue."));
        }

        if (node.TypeName is not IssueTypeName)
        {
            return Result<ProviderReferencedIssue>.Fail(TrackerReadErrors.NotAnIssue());
        }

        IReadOnlyList<string> labels = (node.Labels?.Nodes ?? [])
            .Select(label => label.Name)
            .ToList();

        return Result<ProviderReferencedIssue>.Ok(new ProviderReferencedIssue(
            node.Title ?? string.Empty,
            string.Equals(node.State, "OPEN", StringComparison.OrdinalIgnoreCase),
            labels,
            StrongestClosingState(node.ClosedByPullRequestsReferences?.Nodes ?? []),
            node.Body ?? string.Empty));
    }

    private static ClosingMergeRequestState StrongestClosingState(
        IReadOnlyList<ReferencedIssueClosingPullRequest> closingPullRequests)
    {
        return closingPullRequests
            .Select(pullRequest => pullRequest.State switch
            {
                "MERGED" => ClosingMergeRequestState.Merged,
                "OPEN" => ClosingMergeRequestState.Open,
                "CLOSED" => ClosingMergeRequestState.Closed,
                _ => ClosingMergeRequestState.None,
            })
            .DefaultIfEmpty(ClosingMergeRequestState.None)
            .MaxBy(ClosingStatePrecedence);
    }

    // Merged outranks open, which outranks closed, which outranks none.
    private static int ClosingStatePrecedence(ClosingMergeRequestState state) => state switch
    {
        ClosingMergeRequestState.Merged => 3,
        ClosingMergeRequestState.Open => 2,
        ClosingMergeRequestState.Closed => 1,
        _ => 0,
    };

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

    private sealed record ReferencedIssueData(GraphQlRateLimit? RateLimit, ReferencedIssueRepository? Repository)
        : IReportsRateLimit;

    private sealed record ReferencedIssueRepository(ReferencedIssueNode? IssueOrPullRequest);

    private sealed record ReferencedIssueNode(
        [property: JsonPropertyName("__typename")] string TypeName,
        string? Title,
        string? State,
        string? Body,
        ReferencedIssueLabels? Labels,
        ReferencedIssueClosingPullRequests? ClosedByPullRequestsReferences);

    private sealed record ReferencedIssueLabels(IReadOnlyList<ReferencedIssueLabel> Nodes);

    private sealed record ReferencedIssueLabel(string Name);

    private sealed record ReferencedIssueClosingPullRequests(IReadOnlyList<ReferencedIssueClosingPullRequest> Nodes);

    private sealed record ReferencedIssueClosingPullRequest(string State);
}
