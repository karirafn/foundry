using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal sealed partial class GitHubHttpClient(
    HttpClient httpClient,
    ILogger<GitHubHttpClient> logger,
    DefaultBranchCache defaultBranchCache,
    IProviderRateBudget rateBudget,
    TimeProvider timeProvider) : IGitHubWriteProber
{
    private const string ApiVersion = "2026-03-10";
    private const string TokenExpirationHeader = "github-authentication-token-expiration";
    private const string AllZerosSha = "0000000000000000000000000000000000000000";

    // The rerun endpoint is scoped to {owner}/{repo}, and job id 1 resolves to no job there, so the Actions
    // write probe never re-runs anything: GitHub answers 404 with Actions write and 403 without it.
    private const int ActionsProbeJobId = 1;
    private const int MaxRepositoryPages = 5;
    private const int RepositoriesPerPage = 100;
    private const int MaxIssuePages = 20;
    private const int MaxBlockerPages = 20;
    private const int MaxBranchErrorBodyLength = 500;
    private const int MaxPermissionsLength = 200;
    private const string Ellipsis = "...";

    // Selected by every query: GitHub returns it inside "data", and the budget store keeps only the last reading,
    // so a query selecting fewer fields would overwrite limit and resetAt with null.
    private const string RateLimitSelection = "rateLimit { cost remaining limit resetAt }";

    private const string IssueListQuery = $$"""
        query IssueList($owner: String!, $name: String!, $issuesAfter: String) {
          {{RateLimitSelection}}
          repository(owner: $owner, name: $name) {
            defaultBranchRef { name }
            issues(
              first: 100
              after: $issuesAfter
              states: OPEN
              labels: ["foundry"]
              orderBy: { field: CREATED_AT, direction: ASC }
            ) {
              pageInfo { hasNextPage endCursor }
              nodes {
                number
                title
                url
                author { login }
                labels(first: 100) { nodes { name } }
                blockedBy(first: 50) {
                  totalCount
                  pageInfo { hasNextPage endCursor }
                  nodes {
                    number
                    state
                    repository { nameWithOwner }
                  }
                }
              }
            }
          }
        }
        """;

    private const string IssueStateQuery = $$"""
        query IssueState($owner: String!, $name: String!, $number: Int!) {
          {{RateLimitSelection}}
          repository(owner: $owner, name: $name) {
            issue(number: $number) { state }
          }
        }
        """;

    private const string PrStatusQuery = $$"""
        query PrStatus($owner: String!, $name: String!, $number: Int!) {
          {{RateLimitSelection}}
          repository(owner: $owner, name: $name) {
            pullRequest(number: $number) { id state merged headRefOid isDraft }
          }
        }
        """;

    private const string PrReviewFeedbackQuery = $$"""
        query PrReviewFeedback($owner: String!, $name: String!, $number: Int!) {
          {{RateLimitSelection}}
          repository(owner: $owner, name: $name) {
            pullRequest(number: $number) {
              comments(first: 100) {
                nodes {
                  databaseId
                  body
                  createdAt
                  author { login __typename }
                }
              }
              reviewThreads(first: 100) {
                nodes {
                  isResolved
                  comments(first: 100) {
                    nodes {
                      databaseId
                      body
                      path
                      line
                      originalLine
                      createdAt
                      author { login __typename }
                      pullRequestReview { databaseId }
                    }
                  }
                }
              }
              reviews(first: 50) {
                nodes {
                  databaseId
                  body
                  submittedAt
                  author { login __typename }
                }
              }
            }
          }
        }
        """;

    // statusCheckRollup.state is the canonical verdict; contexts is fetched only to build the
    // failing-check list for Red. The two fields are in one query to avoid a second round-trip
    // for the common case. GitHub may occasionally perturb `state` when many context nodes are
    // fetched at once, but the spec says `state` is the rollup truth — classification is taken
    // from state, and contexts is enumerated only to populate FailingCheck names/urls.
    private const string PrChecksQuery = $$"""
        query PrChecks($owner: String!, $name: String!, $pr: Int!) {
          {{RateLimitSelection}}
          repository(owner: $owner, name: $name) {
            pullRequest(number: $pr) {
              commits(last: 1) {
                nodes {
                  commit {
                    statusCheckRollup {
                      state
                      contexts(first: 100) {
                        nodes {
                          __typename
                          ... on CheckRun {
                            name
                            conclusion
                            detailsUrl
                          }
                          ... on StatusContext {
                            context
                            state
                            targetUrl
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    private const string BlockedByPageQuery = $$"""
        query BlockedByPage($owner: String!, $name: String!, $number: Int!, $blockedByAfter: String) {
          {{RateLimitSelection}}
          repository(owner: $owner, name: $name) {
            issue(number: $number) {
              blockedBy(first: 50, after: $blockedByAfter) {
                pageInfo { hasNextPage endCursor }
                nodes {
                  number
                  state
                  repository { nameWithOwner }
                }
              }
            }
          }
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly JsonSerializerOptions GraphQlJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Task<Result<string>> GetDefaultBranchAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Task.FromResult(Result<string>.Fail(GitHubErrors.InvalidBaseUrl));
        }

        return defaultBranchCache.GetOrFetchAsync(
            apiBaseUrl,
            slug,
            () => FetchDefaultBranchAsync(apiBaseUrl, slug, token, cancellationToken));
    }

    private async Task<Result<string>> FetchDefaultBranchAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string relativePath = $"repos/{owner}/{repo}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<string>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubRepositoryInfoDto? dto = JsonSerializer.Deserialize<GitHubRepositoryInfoDto>(body, JsonOptions);

        return Result<string>.Ok(dto?.DefaultBranch ?? string.Empty);
    }

    public async Task<Result<BranchRules>> GetBranchRulesAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string branch,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<BranchRules>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string encodedBranch = Uri.EscapeDataString(branch);
        string relativePath = $"repos/{owner}/{repo}/rules/branches/{encodedBranch}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            RecordRestHeadroom(response);
            return Result<BranchRules>.Ok(new BranchRules(false, false, false));
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<BranchRules>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitHubBranchRuleDto>? dtos = JsonSerializer.Deserialize<List<GitHubBranchRuleDto>>(body, JsonOptions);

        bool rejectForcePushes = (dtos ?? []).Any(r => r.Type == "non_fast_forward");
        bool rejectDeletion = (dtos ?? []).Any(r => r.Type == "deletion");
        bool rejectDirectPushes = (dtos ?? []).Any(r => r.Type == "pull_request");

        return Result<BranchRules>.Ok(new BranchRules(rejectDirectPushes, rejectForcePushes, rejectDeletion));
    }

    internal async Task<Result<TData>> ExecuteGraphQlAsync<TData>(
        Uri restBaseUrl,
        string query,
        object variables,
        string token,
        CancellationToken cancellationToken,
        Func<HttpResponseMessage, Error>? classifyNonSuccess = null)
        where TData : class
    {
        if (restBaseUrl.Scheme is not "https")
        {
            return Result<TData>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        Uri graphQlEndpoint = DeriveGraphQlEndpoint(restBaseUrl);
        string requestBody = JsonSerializer.Serialize(new { query, variables }, GraphQlJsonOptions);

        using HttpRequestMessage request = new(HttpMethod.Post, graphQlEndpoint);
        AddGitHubHeaders(request, token);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<TData>.Fail((classifyNonSuccess ?? ErrorFromNonSuccess)(response));
        }

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        GraphQlEnvelope? envelope = JsonSerializer.Deserialize<GraphQlEnvelope>(responseBody, GraphQlJsonOptions);

        // GitHub charges an errored query too and still reports its rateLimit block, so the reading is taken first.
        RecordGraphQlHeadroom(envelope?.Data);

        if (envelope?.Errors is { Count: > 0 } errors)
        {
            bool hasRateLimit = errors.Any(
                e => string.Equals(e.Type, "RATE_LIMITED", StringComparison.OrdinalIgnoreCase));

            if (hasRateLimit)
            {
                return Result<TData>.Fail(GitHubErrors.RateLimitExhausted);
            }

            string rawMessage = string.Join("; ", errors.Select(e => e.Message));
            string safeMessage = TruncateWithEllipsis(RedactSecrets(rawMessage), MaxBranchErrorBodyLength);
            Error graphQlError = GitHubErrors.GraphQlError(safeMessage);
            bool onlyNotFound = errors.All(
                e => string.Equals(e.Type, "NOT_FOUND", StringComparison.OrdinalIgnoreCase));

            return Result<TData>.Fail(onlyNotFound ? graphQlError with { Kind = ErrorKind.NotFound } : graphQlError);
        }

        if (envelope?.Data is not { ValueKind: not JsonValueKind.Null } data
            || data.Deserialize<TData>(GraphQlJsonOptions) is not { } typedData)
        {
            return Result<TData>.Fail(
                GitHubErrors.ProviderError("GraphQL response contained neither data nor errors."));
        }

        return Result<TData>.Ok(typedData);
    }

    private void RecordGraphQlHeadroom(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } dataObject
            || !dataObject.TryGetProperty("rateLimit", out JsonElement rateLimitElement)
            || rateLimitElement.ValueKind is not JsonValueKind.Object
            || rateLimitElement.Deserialize<GraphQlRateLimit>(GraphQlJsonOptions) is not { } rateLimit)
        {
            return;
        }

        logger.LogDebug(
            "GitHub GraphQL cost={Cost} remaining={Remaining}",
            rateLimit.Cost,
            rateLimit.Remaining);

        rateBudget.Record(
            ProviderBudgetKey.GitHubGraphQl,
            new RateBudgetReading(rateLimit.Remaining, rateLimit.Limit, rateLimit.ResetAt, timeProvider.GetUtcNow()));
    }

    private static Uri DeriveGraphQlEndpoint(Uri restBaseUrl)
    {
        // github.com: https://api.github.com → https://api.github.com/graphql
        if (string.Equals(restBaseUrl.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri("https://api.github.com/graphql");
        }

        // GHES: https://<host>/api/v3/ → https://<host>/api/graphql
        string origin = $"{restBaseUrl.Scheme}://{restBaseUrl.Authority}";
        return new Uri($"{origin}/api/graphql");
    }

    public async Task<Result<IssueListingWithDependencies>> GetIssuesWithDependenciesAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IssueListingWithDependencies>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string expectedFullName = $"{slug.Owner}/{slug.Name}";
        List<ProviderIssue> allIssues = [];
        Dictionary<int, IReadOnlyList<int>> blockedByMap = [];
        string? cursor = null;

        for (int page = 1; page <= MaxIssuePages; page++)
        {
            object variables = new { owner = slug.Owner, name = slug.Name, issuesAfter = cursor };
            Result<IssueListData> pageResult = await ExecuteGraphQlAsync<IssueListData>(
                apiBaseUrl, IssueListQuery, variables, token, cancellationToken);

            if (pageResult is not Result<IssueListData>.Success pageSuccess)
            {
                Error error = ((Result<IssueListData>.Failure)pageResult).Error;
                return Result<IssueListingWithDependencies>.Fail(error);
            }

            IssueListData data = pageSuccess.Value;
            GraphQlIssueConnection? connection = data.Repository?.Issues;
            IReadOnlyList<GraphQlIssueNode> nodes = connection?.Nodes ?? [];

            foreach (GraphQlIssueNode node in nodes)
            {
                IReadOnlyList<string> labels = node.Labels.Nodes
                    .Select(l => l.Name)
                    .ToList();

                allIssues.Add(new ProviderIssue(
                    Number: node.Number,
                    Title: node.Title,
                    Author: node.Author?.Login ?? string.Empty,
                    Url: node.Url,
                    Labels: labels,
                    IssueKindLabel: LabelClassifier.ClassifyKind(labels)));

                Result<IReadOnlyList<int>> blockersResult = await CollectBlockersAsync(
                    apiBaseUrl, slug, node, expectedFullName, token, cancellationToken);

                if (blockersResult is not Result<IReadOnlyList<int>>.Success blockersSuccess)
                {
                    Error error = ((Result<IReadOnlyList<int>>.Failure)blockersResult).Error;
                    return Result<IssueListingWithDependencies>.Fail(error);
                }

                IReadOnlyList<int> blockers = blockersSuccess.Value;
                if (blockers.Count > 0)
                {
                    blockedByMap[node.Number] = blockers;
                }
            }

            GraphQlPageInfo? pageInfo = connection?.PageInfo;
            bool hasNextPage = pageInfo?.HasNextPage ?? false;

            if (!hasNextPage)
            {
                IssueListing listing = new(allIssues, IsComplete: true);
                return Result<IssueListingWithDependencies>.Ok(
                    new IssueListingWithDependencies(listing, blockedByMap));
            }

            cursor = pageInfo?.EndCursor;
        }

        IssueListing cappedListing = new(allIssues, IsComplete: false);
        return Result<IssueListingWithDependencies>.Ok(
            new IssueListingWithDependencies(cappedListing, blockedByMap));
    }

    private async Task<Result<IReadOnlyList<int>>> CollectBlockersAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        GraphQlIssueNode node,
        string expectedFullName,
        string token,
        CancellationToken cancellationToken)
    {
        GraphQlBlockedByConnection? blockedByConnection = node.BlockedBy;
        List<GraphQlBlockerNode> allBlockerNodes = [.. (blockedByConnection?.Nodes ?? [])];

        if (blockedByConnection?.PageInfo.HasNextPage is true)
        {
            string? blockedByCursor = blockedByConnection.PageInfo.EndCursor;

            for (int bbPage = 1; bbPage <= MaxBlockerPages; bbPage++)
            {
                object bbVariables = new
                {
                    owner = slug.Owner,
                    name = slug.Name,
                    number = node.Number,
                    blockedByAfter = blockedByCursor,
                };

                Result<BlockedByPageData> bbResult = await ExecuteGraphQlAsync<BlockedByPageData>(
                    apiBaseUrl, BlockedByPageQuery, bbVariables, token, cancellationToken);

                if (bbResult is not Result<BlockedByPageData>.Success bbSuccess)
                {
                    Error error = ((Result<BlockedByPageData>.Failure)bbResult).Error;
                    return Result<IReadOnlyList<int>>.Fail(error);
                }

                GraphQlBlockedByConnection? bbConnection =
                    bbSuccess.Value.Repository?.Issue?.BlockedBy;

                allBlockerNodes.AddRange(bbConnection?.Nodes ?? []);

                if (bbConnection?.PageInfo.HasNextPage is not true)
                {
                    break;
                }

                if (bbPage == MaxBlockerPages)
                {
                    return Result<IReadOnlyList<int>>.Fail(
                        GitHubErrors.GraphQlError(
                            $"blockedBy pagination for issue #{node.Number} exceeded the {MaxBlockerPages}-page cap."));
                }

                blockedByCursor = bbConnection.PageInfo.EndCursor;
            }
        }

        List<int> blockers = allBlockerNodes
            .Where(b => string.Equals(
                b.Repository.NameWithOwner,
                expectedFullName,
                StringComparison.OrdinalIgnoreCase))
            .Where(b => !string.Equals(b.State, "CLOSED", StringComparison.OrdinalIgnoreCase))
            .Select(b => b.Number)
            .ToList();

        return Result<IReadOnlyList<int>>.Ok(blockers);
    }

    public async Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueNumber,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<int>>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string relativePath = $"repos/{owner}/{repo}/issues/{issueNumber}/dependencies/blocked_by";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<int>>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitHubDependencyDto>? dtos = JsonSerializer.Deserialize<List<GitHubDependencyDto>>(body, JsonOptions);

        string expectedFullName = $"{slug.Owner}/{slug.Name}";
        IReadOnlyList<int> issueNumbers = (dtos ?? [])
            .Where(dto => string.Equals(dto.Repository.FullName, expectedFullName, StringComparison.OrdinalIgnoreCase))
            .Where(dto => !string.Equals(dto.State, "closed", StringComparison.OrdinalIgnoreCase))
            .Select(dto => dto.Number)
            .ToList();

        return Result<IReadOnlyList<int>>.Ok(issueNumbers);
    }

    public async Task<Result<bool>> IsIssueClosedAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueNumber,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<bool>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        object variables = new { owner = slug.Owner, name = slug.Name, number = issueNumber };
        Result<IssueStateData> dataResult = await ExecuteGraphQlAsync<IssueStateData>(
            apiBaseUrl, IssueStateQuery, variables, token, cancellationToken);

        if (dataResult is not Result<IssueStateData>.Success dataSuccess)
        {
            Error error = ((Result<IssueStateData>.Failure)dataResult).Error;
            return Result<bool>.Fail(error);
        }

        string? state = dataSuccess.Value.Repository?.Issue?.State;
        bool isClosed = string.Equals(state, "CLOSED", StringComparison.OrdinalIgnoreCase);
        return Result<bool>.Ok(isClosed);
    }

    public async Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<PullRequestStatus>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        if (!TryParsePrNumber(pullRequestUrl, out int prNumber))
        {
            return Result<PullRequestStatus>.Fail(GitHubErrors.InvalidPullRequestUrl);
        }

        object variables = new { owner = slug.Owner, name = slug.Name, number = prNumber };
        Result<PrStatusData> dataResult = await ExecuteGraphQlAsync<PrStatusData>(
            apiBaseUrl, PrStatusQuery, variables, token, cancellationToken);

        if (dataResult is not Result<PrStatusData>.Success dataSuccess)
        {
            Error error = ((Result<PrStatusData>.Failure)dataResult).Error;
            return Result<PullRequestStatus>.Fail(error);
        }

        GraphQlPullRequestStatus? pr = dataSuccess.Value.Repository?.PullRequest;
        bool isClosed = string.Equals(pr?.State, "CLOSED", StringComparison.OrdinalIgnoreCase);
        bool isMerged = pr?.Merged ?? false;
        return Result<PullRequestStatus>.Ok(new PullRequestStatus(isClosed, isMerged, pr?.HeadRefOid, pr?.IsDraft ?? false));
    }

    public async Task<Result<Checks>> GetChecksAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<Checks>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        if (!TryParsePrNumber(pullRequestUrl, out int prNumber))
        {
            return Result<Checks>.Fail(GitHubErrors.InvalidPullRequestUrl);
        }

        object variables = new { owner = slug.Owner, name = slug.Name, pr = prNumber };
        Result<PrChecksData> dataResult = await ExecuteGraphQlAsync<PrChecksData>(
            apiBaseUrl, PrChecksQuery, variables, token, cancellationToken, ClassifyUnreadable);

        if (dataResult is not Result<PrChecksData>.Success dataSuccess)
        {
            Error error = ((Result<PrChecksData>.Failure)dataResult).Error;
            if (string.Equals(error.Code, "GitHub.Unreadable", StringComparison.Ordinal))
            {
                return Result<Checks>.Ok(new Checks.Unreadable());
            }

            return Result<Checks>.Fail(error);
        }

        IReadOnlyList<PrChecksCommitEdge>? commitNodes = dataSuccess
            .Value
            .Repository?
            .PullRequest?
            .Commits?
            .Nodes;

        PrChecksStatusCheckRollup? rollup = commitNodes is { Count: > 0 }
            ? commitNodes[0].Commit?.StatusCheckRollup
            : null;

        if (rollup is null)
        {
            return Result<Checks>.Ok(new Checks.NoneDetected());
        }

        IReadOnlyList<FailingCheck> failing = BuildFailingChecks(rollup.Contexts?.Nodes ?? (IReadOnlyList<PrChecksContextUnionNode>)[]);
        return Result<Checks>.Ok(Checks.FromGitHub(rollup.State, failing));
    }

    private static List<FailingCheck> BuildFailingChecks(
        IReadOnlyList<PrChecksContextUnionNode> contextNodes)
    {
        List<FailingCheck> failing = [];
        foreach (PrChecksContextUnionNode node in contextNodes)
        {
            if (string.Equals(node.Typename, "CheckRun", StringComparison.Ordinal)
                && Checks.IsGitHubCheckRunFailure(node.Conclusion))
            {
                failing.Add(new FailingCheck(
                    node.Name ?? string.Empty,
                    node.Conclusion ?? string.Empty,
                    node.DetailsUrl,
                    Checks.IsGitHubCheckRunInfrastructureFailure(node.Conclusion)));
            }
            else if (string.Equals(node.Typename, "StatusContext", StringComparison.Ordinal)
                && Checks.IsGitHubStatusContextFailure(node.State))
            {
                failing.Add(new FailingCheck(
                    node.Context ?? string.Empty,
                    node.State ?? string.Empty,
                    node.TargetUrl,
                    Checks.IsGitHubStatusContextInfrastructureFailure(node.State)));
            }
        }

        return failing;
    }

    // A 401, or a 403 that is not rate limiting, means the token cannot read the pull request: the caller reports
    // it as unreadable rather than as a failure.
    private static Error ClassifyUnreadable(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Unauthorized
        || (response.StatusCode is HttpStatusCode.Forbidden && !IsRateLimited(response))
            ? GitHubErrors.Unreadable
            : ErrorFromNonSuccess(response);

    public async Task<Result<IReadOnlyList<ProviderComment>>> GetPullRequestReviewFeedbackAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<ProviderComment>>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        if (!TryParsePrNumber(pullRequestUrl, out int prNumber))
        {
            return Result<IReadOnlyList<ProviderComment>>.Fail(GitHubErrors.InvalidPullRequestUrl);
        }

        object variables = new { owner = slug.Owner, name = slug.Name, number = prNumber };
        Result<PrReviewFeedbackData> dataResult = await ExecuteGraphQlAsync<PrReviewFeedbackData>(
            apiBaseUrl, PrReviewFeedbackQuery, variables, token, cancellationToken);

        if (dataResult is not Result<PrReviewFeedbackData>.Success dataSuccess)
        {
            Error error = ((Result<PrReviewFeedbackData>.Failure)dataResult).Error;
            return Result<IReadOnlyList<ProviderComment>>.Fail(error);
        }

        PrReviewFeedbackPullRequest? pullRequest = dataSuccess.Value.Repository?.PullRequest;
        List<ProviderComment> providerComments = [];

        // Review-thread (inline) comments are processed first so their richer context
        // (FilePath, Line) wins when the same comment also appears in the top-level
        // conversation surface. The de-dup key uses the stable databaseId when available;
        // it falls back to (Body, CreatedAt, AuthorLogin) for nodes that omit the id.
        HashSet<long> deduplicatedIds = [];
        HashSet<(string Body, DateTimeOffset CreatedAt, string AuthorLogin)> deduplicatedKeys = [];

        IReadOnlyList<GraphQlReviewThreadNode> reviewThreadNodes =
            pullRequest?.ReviewThreads?.Nodes ?? [];

        foreach (GraphQlReviewThreadNode thread in reviewThreadNodes)
        {
            bool threadResolved = thread.IsResolved;
            IReadOnlyList<GraphQlReviewThreadCommentNode> threadComments =
                thread.Comments?.Nodes ?? [];

            foreach (GraphQlReviewThreadCommentNode comment in threadComments)
            {
                string authorLogin = comment.Author?.Login ?? string.Empty;
                bool authorIsBot = string.Equals(comment.Author?.Typename, "Bot", StringComparison.Ordinal);
                int? line = comment.Line ?? comment.OriginalLine;
                string? sanitizedPath = SanitizeFilePath(comment.Path);

                if (comment.DatabaseId.HasValue)
                {
                    if (!deduplicatedIds.Add(comment.DatabaseId.Value))
                    {
                        continue;
                    }

                    // Seed the content-key set so a later conversation-surface copy with a null
                    // databaseId (which falls through to the key-based branch) is still de-duped.
                    deduplicatedKeys.Add((comment.Body, comment.CreatedAt, authorLogin));
                }
                else if (!deduplicatedKeys.Add((comment.Body, comment.CreatedAt, authorLogin)))
                {
                    continue;
                }

                providerComments.Add(new ProviderComment(
                    Id: ProviderCommentId.FromProvider(comment.DatabaseId),
                    Body: comment.Body,
                    AuthorLogin: authorLogin,
                    AuthorIsBot: authorIsBot,
                    IsSystem: false,
                    CreatedAt: comment.CreatedAt,
                    FilePath: sanitizedPath,
                    Line: line,
                    Origin: CommentOrigin.ReviewThread,
                    ThreadResolved: threadResolved,
                    ReviewId: ProviderCommentId.FromProvider(comment.PullRequestReview?.DatabaseId)));
            }
        }

        IReadOnlyList<GraphQlConversationCommentNode> conversationNodes =
            pullRequest?.Comments?.Nodes ?? [];

        foreach (GraphQlConversationCommentNode node in conversationNodes)
        {
            string authorLogin = node.Author?.Login ?? string.Empty;
            bool authorIsBot = string.Equals(node.Author?.Typename, "Bot", StringComparison.Ordinal);

            // Skip the conversation-surface copy when the inline copy (with FilePath/Line) was
            // already collected from reviewThreads. The id-based check is authoritative; the
            // key-based check is the fallback for nodes without a databaseId.
            if (node.DatabaseId.HasValue)
            {
                if (!deduplicatedIds.Add(node.DatabaseId.Value))
                {
                    continue;
                }

                // Seed the content-key set so a later null-databaseId copy of this comment
                // (on any remaining surface) is still de-duped by content key.
                deduplicatedKeys.Add((node.Body, node.CreatedAt, authorLogin));
            }
            else if (!deduplicatedKeys.Add((node.Body, node.CreatedAt, authorLogin)))
            {
                continue;
            }

            providerComments.Add(new ProviderComment(
                Id: ProviderCommentId.FromProvider(node.DatabaseId),
                Body: node.Body,
                AuthorLogin: authorLogin,
                AuthorIsBot: authorIsBot,
                IsSystem: false,
                CreatedAt: node.CreatedAt,
                FilePath: null,
                Line: null,
                Origin: CommentOrigin.Conversation,
                ThreadResolved: false));
        }

        IReadOnlyList<GraphQlReviewNode> reviewNodes =
            pullRequest?.Reviews?.Nodes ?? [];

        foreach (GraphQlReviewNode review in reviewNodes)
        {
            if (string.IsNullOrWhiteSpace(review.Body) || review.SubmittedAt is null)
            {
                continue;
            }

            string authorLogin = review.Author?.Login ?? string.Empty;
            bool authorIsBot = string.Equals(review.Author?.Typename, "Bot", StringComparison.Ordinal);

            providerComments.Add(new ProviderComment(
                Id: ProviderCommentId.FromProvider(review.DatabaseId),
                Body: review.Body,
                AuthorLogin: authorLogin,
                AuthorIsBot: authorIsBot,
                IsSystem: false,
                CreatedAt: review.SubmittedAt.Value,
                FilePath: null,
                Line: null,
                Origin: CommentOrigin.ReviewSummary,
                ThreadResolved: false,
                ReviewId: ProviderCommentId.FromProvider(review.DatabaseId)));
        }

        return Result<IReadOnlyList<ProviderComment>>.Ok(providerComments);
    }

    public async Task<Result<TokenValidationOutcome>> ValidateTokenAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<TokenValidationOutcome>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), "user");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            return Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.AuthenticationFailed());
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<TokenValidationOutcome>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubUserDto? userDto = DeserializeUserDto(responseBody, apiBaseUrl, (int)response.StatusCode);

        if (userDto is null)
        {
            return Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.IdentityUnresolved());
        }

        string? login = string.IsNullOrEmpty(userDto.Login) ? null : userDto.Login;

        if (login is null && !string.IsNullOrEmpty(userDto.Username))
        {
            return Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.ProviderMismatch(ProviderTypes.GitLab));
        }

        if (login is null)
        {
            return Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.IdentityUnresolved());
        }

        TokenExpiry tokenExpiry = ReadTokenExpiry(response, apiBaseUrl);

        // A fine-grained token's permissions are checked per repository by the probes, so nothing here
        // reads scopes.
        return GitHubTokenKind.WrongKindOf(token) is WrongTokenKind wrongKind
            ? Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.TokenOfWrongKind(login, wrongKind, tokenExpiry))
            : Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.Authenticated(login, tokenExpiry));
    }

    public async Task<Result<WritePermissionProbeResult>> ProbeContentsWriteAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<WritePermissionProbeResult>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string probeName = $"foundry-probe-{Guid.NewGuid():N}";
        string relativePath = $"repos/{owner}/{repo}/git/refs";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        // The all-zeros SHA never resolves, so a token with write access gets 422 and no ref is created.
        string body = JsonSerializer.Serialize(
            new { @ref = $"refs/heads/{probeName}", sha = AllZerosSha },
            JsonOptions);

        using HttpRequestMessage request = new(HttpMethod.Post, requestUri);
        AddGitHubHeaders(request, token);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        RecordRestHeadroom(response);
        return ClassifyProbeResponse(response, WritePermission.Contents);
    }

    public async Task<Result<WritePermissionProbeResult>> ProbeIssuesWriteAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<WritePermissionProbeResult>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string relativePath = $"repos/{owner}/{repo}/issues";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Post, requestUri);
        AddGitHubHeaders(request, token);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        RecordRestHeadroom(response);
        return ClassifyProbeResponse(response, WritePermission.Issues);
    }

    public async Task<Result<WritePermissionProbeResult>> ProbePullRequestsWriteAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<WritePermissionProbeResult>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string relativePath = $"repos/{owner}/{repo}/pulls";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Post, requestUri);
        AddGitHubHeaders(request, token);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        RecordRestHeadroom(response);
        return ClassifyProbeResponse(response, WritePermission.PullRequests);
    }

    public async Task<Result<WritePermissionProbeResult>> ProbeAdministrationWriteAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<WritePermissionProbeResult>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        using HttpResponseMessage response = await SendEmptyPostAsync(apiBaseUrl, slug, "rulesets", token, cancellationToken);

        RecordRestHeadroom(response);
        return ClassifyStrictWriteProbeResponse(response, WritePermission.Administration);
    }

    public async Task<Result<WritePermissionProbeResult>> ProbeWebhooksWriteAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<WritePermissionProbeResult>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        using HttpResponseMessage response = await SendEmptyPostAsync(apiBaseUrl, slug, "hooks", token, cancellationToken);

        RecordRestHeadroom(response);
        return ClassifyStrictWriteProbeResponse(response, WritePermission.Webhooks);
    }

    public async Task<Result<WritePermissionProbeResult>> ProbeCommitStatusesReadAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<WritePermissionProbeResult>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        Result<string> defaultBranch = await GetDefaultBranchAsync(apiBaseUrl, slug, token, cancellationToken);
        if (defaultBranch is not Result<string>.Success { Value: string defaultBranchName })
        {
            return defaultBranch is Result<string>.Failure branchFailure
                ? Result<WritePermissionProbeResult>.Fail(branchFailure.Error)
                : throw new UnreachableException("Unhandled Result variant.");
        }

        string branch = Uri.EscapeDataString(defaultBranchName);
        using HttpResponseMessage response = await SendGetAsync(
            apiBaseUrl, slug, $"commits/{branch}/statuses?per_page=1", token, cancellationToken);

        RecordRestHeadroom(response);
        return ClassifyReadProbeResponse(response, WritePermission.CommitStatuses);
    }

    public async Task<Result<WritePermissionProbeResult>> ProbeActionsWriteAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<WritePermissionProbeResult>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        using HttpResponseMessage response = await SendEmptyPostAsync(
            apiBaseUrl, slug, $"actions/jobs/{ActionsProbeJobId}/rerun", token, cancellationToken);

        RecordRestHeadroom(response);
        return ClassifyActionsWriteProbeResponse(response);
    }

    /// <summary>
    /// Runs every probe the permission list names, in its order, and returns every permission the token
    /// lacks so the operator learns them all in one pass. Stops at the first indeterminate probe.
    /// </summary>
    public async Task<Result<WritePermissionProbeResult>> ProbeWriteAccessAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        List<WritePermission> missing = [];
        foreach (WritePermission permission in TokenPermissions.GitHubProbes)
        {
            Result<WritePermissionProbeResult> result = await ProbeAsync(
                permission, apiBaseUrl, slug, token, cancellationToken);

            switch (result)
            {
                case Result<WritePermissionProbeResult>.Success { Value: WritePermissionProbeResult.Missing probeMissing }:
                    missing.AddRange(probeMissing.Permissions);
                    break;
                case Result<WritePermissionProbeResult>.Success:
                    break;
                default:
                    return result;
            }
        }

        return missing.Count == 0
            ? Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Granted())
            : Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Missing(missing));
    }

    private Task<Result<WritePermissionProbeResult>> ProbeAsync(
        WritePermission permission,
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken) =>
        permission switch
        {
            WritePermission.Contents => ProbeContentsWriteAsync(apiBaseUrl, slug, token, cancellationToken),
            WritePermission.Issues => ProbeIssuesWriteAsync(apiBaseUrl, slug, token, cancellationToken),
            WritePermission.PullRequests => ProbePullRequestsWriteAsync(apiBaseUrl, slug, token, cancellationToken),
            WritePermission.Administration => ProbeAdministrationWriteAsync(apiBaseUrl, slug, token, cancellationToken),
            WritePermission.Webhooks => ProbeWebhooksWriteAsync(apiBaseUrl, slug, token, cancellationToken),
            WritePermission.CommitStatuses => ProbeCommitStatusesReadAsync(apiBaseUrl, slug, token, cancellationToken),
            WritePermission.Actions => ProbeActionsWriteAsync(apiBaseUrl, slug, token, cancellationToken),
            _ => throw new UnreachableException($"No probe for {permission}."),
        };

    private async Task<HttpResponseMessage> SendEmptyPostAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string repositoryPath,
        string token,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, RepositoryUri(apiBaseUrl, slug, repositoryPath));
        AddGitHubHeaders(request, token);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendGetAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string repositoryPath,
        string token,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, RepositoryUri(apiBaseUrl, slug, repositoryPath));
        AddGitHubHeaders(request, token);
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static Uri RepositoryUri(Uri apiBaseUrl, RepositorySlug slug, string repositoryPath)
    {
        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        return new Uri(EnsureTrailingSlash(apiBaseUrl), $"repos/{owner}/{repo}/{repositoryPath}");
    }

    public async Task<Result<bool>> GetPushPermissionAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<bool>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string relativePath = $"repos/{owner}/{repo}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddGitHubHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<bool>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubRepoPermissionsResponseDto? dto =
            JsonSerializer.Deserialize<GitHubRepoPermissionsResponseDto>(body, JsonOptions);

        return Result<bool>.Ok(dto?.Permissions?.Push ?? false);
    }

    public async Task<Result<IReadOnlyList<ProviderRepository>>> ListRepositoriesAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<ProviderRepository>>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        List<ProviderRepository> repositories = [];

        for (int page = 1; page <= MaxRepositoryPages; page++)
        {
            string relativePath = $"user/repos?sort=full_name&per_page={RepositoriesPerPage}&page={page}";
            Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

            using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result<IReadOnlyList<ProviderRepository>>.Fail(ErrorFromNonSuccess(response));
            }

            RecordRestHeadroom(response);

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            List<GitHubRepositoryListItemDto>? dtos =
                JsonSerializer.Deserialize<List<GitHubRepositoryListItemDto>>(body, JsonOptions);

            List<GitHubRepositoryListItemDto> pageItems = dtos ?? [];
            foreach (GitHubRepositoryListItemDto dto in pageItems)
            {
                repositories.Add(new ProviderRepository(dto.FullName, dto.Private, dto.Permissions?.Push ?? false));
            }

            if (pageItems.Count < RepositoriesPerPage)
            {
                break;
            }
        }

        return Result<IReadOnlyList<ProviderRepository>>.Ok(repositories);
    }

    public async Task<Result<bool>> CreateBranchAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string defaultBranch,
        string branchName,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<bool>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string encodedBranch = Uri.EscapeDataString(defaultBranch);

        string getRefPath = $"repos/{owner}/{repo}/git/refs/heads/{encodedBranch}";
        Uri getRefUri = new(EnsureTrailingSlash(apiBaseUrl), getRefPath);

        using HttpRequestMessage getRefRequest = new(HttpMethod.Get, getRefUri);
        getRefRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        getRefRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        getRefRequest.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        getRefRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage getRefResponse = await httpClient.SendAsync(getRefRequest, cancellationToken);

        if (!getRefResponse.IsSuccessStatusCode)
        {
            return Result<bool>.Fail(ErrorFromNonSuccess(getRefResponse));
        }

        RecordRestHeadroom(getRefResponse);

        string getRefBody = await getRefResponse.Content.ReadAsStringAsync(cancellationToken);
        GitHubGitRefDto? gitRef = JsonSerializer.Deserialize<GitHubGitRefDto>(getRefBody, JsonOptions);
        string sha = gitRef?.Object?.Sha ?? string.Empty;

        string createRefsPath = $"repos/{owner}/{repo}/git/refs";
        Uri createRefsUri = new(EnsureTrailingSlash(apiBaseUrl), createRefsPath);

        string createBody = JsonSerializer.Serialize(
            new { @ref = $"refs/heads/{branchName}", sha },
            JsonOptions);

        using HttpRequestMessage createRefRequest = new(HttpMethod.Post, createRefsUri);
        createRefRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createRefRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        createRefRequest.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        createRefRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));
        createRefRequest.Content = new StringContent(createBody, Encoding.UTF8, "application/json");

        using HttpResponseMessage createRefResponse = await httpClient.SendAsync(createRefRequest, cancellationToken);

        if (createRefResponse.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            RecordRestHeadroom(createRefResponse);
            return Result<bool>.Ok(false);
        }

        if (!createRefResponse.IsSuccessStatusCode)
        {
            Error branchError = await ErrorFromBranchCreationFailureAsync(createRefResponse, slug, cancellationToken);
            return Result<bool>.Fail(branchError);
        }

        RecordRestHeadroom(createRefResponse);
        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// Deletes <paramref name="branchName"/> from the repository. Returns <c>true</c> when the
    /// branch was deleted and <c>false</c> when it was already absent (404 or 422 "Reference does
    /// not exist"), so a repeated discard is idempotent.
    /// </summary>
    public async Task<Result<bool>> DeleteBranchAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string branchName,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<bool>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        // Encode each segment but keep the '/' separators: the git refs API addresses a ref by path.
        string encodedBranch = string.Join('/', branchName.Split('/').Select(Uri.EscapeDataString));
        string relativePath = $"repos/{owner}/{repo}/git/refs/heads/{encodedBranch}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Delete, requestUri);
        AddGitHubHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
        {
            RecordRestHeadroom(response);
            return Result<bool>.Ok(false);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<bool>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);
        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// Reads where <paramref name="branchName"/> points with <c>GET repos/{owner}/{repo}/git/ref/heads/{branch}</c>
    /// (the singular <c>ref</c>, which matches the ref exactly). GitHub answers 404 when the ref does not exist, which
    /// is <see cref="BranchHead.Absent"/>; any other non-success response fails.
    /// </summary>
    public async Task<Result<BranchHead>> GetBranchHeadAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string branchName,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<BranchHead>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        // Encode each segment but keep the '/' separators: the git refs API addresses a ref by path.
        string encodedBranch = string.Join('/', branchName.Split('/').Select(Uri.EscapeDataString));
        string relativePath = $"repos/{owner}/{repo}/git/ref/heads/{encodedBranch}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddGitHubHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            RecordRestHeadroom(response);
            return Result<BranchHead>.Ok(new BranchHead.Absent());
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<BranchHead>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        string? sha = JsonSerializer.Deserialize<GitHubRefDto>(body, JsonOptions)?.Object?.Sha;

        return sha is not null && GitObjectIdRegex().IsMatch(sha)
            ? Result<BranchHead>.Ok(new BranchHead.Present(sha))
            : Result<BranchHead>.Fail(GitHubErrors.ProviderError("GitHub returned a branch ref without a commit SHA."));
    }

    public async Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string defaultBranch,
        string branchName,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<BranchCommitSummary>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string encodedDefault = Uri.EscapeDataString(defaultBranch);
        string encodedBranch = Uri.EscapeDataString(branchName);
        string relativePath = $"repos/{owner}/{repo}/compare/{encodedDefault}...{encodedBranch}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<BranchCommitSummary>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubCompareDto? dto = JsonSerializer.Deserialize<GitHubCompareDto>(body, JsonOptions);

        int commitCount = dto?.AheadBy ?? 0;
        IReadOnlyList<GitHubCommitRefDto> commits = dto?.Commits ?? [];
        string? rawSha = commitCount > 0 && commits.Count > 0 ? commits[^1].Sha : null;
        string? latestSha = rawSha is not null && GitObjectIdRegex().IsMatch(rawSha) ? rawSha : null;

        return Result<BranchCommitSummary>.Ok(new BranchCommitSummary(commitCount, latestSha));
    }

    public async Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string branchName,
        string token,
        CommitSha? publishedHead,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<MergeRequestByBranch>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string encodedHead = Uri.EscapeDataString($"{slug.Owner}:{branchName}");
        // GitHub retains the head.ref metadata on a PR record after the source branch is deleted,
        // so ?head={owner}:{branch}&state=all returns the merged PR even when the branch is gone.
        // API version: 2026-03-10 (set in AddGitHubHeaders via X-GitHub-Api-Version header).
        string relativePath = $"repos/{owner}/{repo}/pulls?head={encodedHead}&state=all";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddGitHubHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<MergeRequestByBranch>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitHubPullRequestStateDto>? dtos =
            JsonSerializer.Deserialize<List<GitHubPullRequestStateDto>>(body, JsonOptions);

        List<MergeRequestCandidate> candidates = (dtos ?? []).Select(ToCandidate).ToList();

        return Result<MergeRequestByBranch>.Ok(MergeRequestSelection.Select(candidates, publishedHead));
    }

    private static MergeRequestCandidate ToCandidate(GitHubPullRequestStateDto dto)
    {
        MergeRequestPresence presence = dto.MergedAt is not null
            ? MergeRequestPresence.Merged
            : string.Equals(dto.State, "open", StringComparison.OrdinalIgnoreCase)
                ? MergeRequestPresence.Open
                : MergeRequestPresence.Closed;
        CommitSha? head = CommitSha.TryFrom(dto.Head?.Sha, out CommitSha sha) ? sha : null;

        return new MergeRequestCandidate(presence, dto.HtmlUrl, dto.UpdatedAt, head);
    }

    /// <summary>
    /// Classifies the Administration and Webhooks probes. Unlike <see cref="ClassifyProbeResponse"/> a 404
    /// is not read as granted: a server without the endpoint (GitHub Enterprise Server without rulesets)
    /// answers 404, and that must never make a repository eligible.
    /// </summary>
    private static Result<WritePermissionProbeResult> ClassifyStrictWriteProbeResponse(
        HttpResponseMessage response,
        WritePermission permission) =>
        response.StatusCode switch
        {
            HttpStatusCode.UnprocessableEntity => Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Granted()),
            _ => ClassifyRefusal(response, permission),
        };

    /// <summary>
    /// Classifies the Actions write probe. GitHub checks the token's Actions write permission before it looks
    /// the job up, so a 404 for the nonexistent job means granted and a 403 means missing.
    /// </summary>
    private static Result<WritePermissionProbeResult> ClassifyActionsWriteProbeResponse(HttpResponseMessage response) =>
        response.StatusCode switch
        {
            HttpStatusCode.NotFound => Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Granted()),
            _ => ClassifyRefusal(response, WritePermission.Actions),
        };

    private static Result<WritePermissionProbeResult> ClassifyReadProbeResponse(
        HttpResponseMessage response,
        WritePermission permission) =>
        response.StatusCode switch
        {
            HttpStatusCode.OK => Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Granted()),
            _ => ClassifyRefusal(response, permission),
        };

    private static Result<WritePermissionProbeResult> ClassifyRefusal(
        HttpResponseMessage response,
        WritePermission permission) =>
        response.StatusCode switch
        {
            HttpStatusCode.Forbidden when IsRateLimited(response) =>
                Result<WritePermissionProbeResult>.Fail(GitHubErrors.RateLimitExhausted),
            HttpStatusCode.Forbidden => Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Missing([permission])),
            _ => Result<WritePermissionProbeResult>.Fail(ErrorFromNonSuccess(response)),
        };

    private static Result<WritePermissionProbeResult> ClassifyProbeResponse(
        HttpResponseMessage response,
        WritePermission permission)
    {
        return response.StatusCode switch
        {
            // 422 means the token passed the permission check and only the deliberately invalid payload was refused.
            HttpStatusCode.UnprocessableEntity => Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Granted()),
            // 404 cannot confirm a denial, so the probe does not block; later operations surface the real error.
            HttpStatusCode.NotFound => Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Granted()),
            // A 403 carrying rate-limit headers is a transient exhaustion, not a permission denial.
            // Returning Fail here prevents the caller from recording a spurious Denied verdict.
            HttpStatusCode.Forbidden when IsRateLimited(response) =>
                Result<WritePermissionProbeResult>.Fail(GitHubErrors.RateLimitExhausted),
            HttpStatusCode.Forbidden => Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Missing([permission])),
            // Any other status — including 401 (token expired or revoked mid-probe) and any
            // unexpected 2xx (which would mean the probe actually created an object) — is
            // indeterminate and fails closed to preserve non-destructiveness and fail-closed
            // semantics. Never map these to Granted.
            _ => Result<WritePermissionProbeResult>.Fail(ErrorFromNonSuccess(response)),
        };
    }

    private static void AddGitHubHeaders(HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        string uriString = uri.ToString();
        return uriString.EndsWith('/') ? uri : new Uri(uriString + '/');
    }

    // Matches a SHA-1 hex object id (up to 40 hex chars), deliberately aligned with the
    // HasMaxLength(40) column on ActiveRun.LastObservedCommitSha. The bound is intentionally
    // NOT widened to {1,64} for SHA-256: a 64-char SHA-256 id would exceed the storage column
    // and reintroduce the oversized-value persistence risk this guard was added to prevent.
    // Supporting SHA-256 requires widening the storage column first (out of scope). Treating
    // an out-of-range value (including a 64-char SHA-256 id) as absent is the safe behavior —
    // the SHA is a change-detection key only, so a missing value causes a re-fetch, not data loss.
    [GeneratedRegex(@"^[0-9a-fA-F]{1,40}$")]
    private static partial Regex GitObjectIdRegex();

    [GeneratedRegex(@"/pull/(\d+)(?:[/?#]|$)")]
    private static partial Regex PrNumberRegex();

    [GeneratedRegex(
        @"(?:glpat-|ghp_|github_pat_|gho_|sk-ant-)\S+",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex KnownTokenPattern();

    // Mirrors SecretRedactor.HttpsUserinfoPattern — scoped here to GitHub response bodies
    // (no env-var pass needed in this context).
    [GeneratedRegex(
        @"https://[^@/\s]+@",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex HttpsUserinfoPattern();

    private static bool TryParsePrNumber(string pullRequestUrl, out int prNumber)
    {
        Match match = PrNumberRegex().Match(pullRequestUrl);
        if (match.Success && int.TryParse(match.Groups[1].Value, out prNumber))
        {
            return true;
        }

        prNumber = 0;
        return false;
    }

    /// <summary>
    /// Maps the undocumented <c>github-authentication-token-expiration</c> header (observed as
    /// <c>2026-11-22 10:16:35 UTC</c>). A missing header is <see cref="TokenExpiry.Unavailable"/> rather than
    /// <see cref="TokenExpiry.Never"/>: GitHub omitting it has not been observed to mean "never expires".
    /// </summary>
    private TokenExpiry ReadTokenExpiry(HttpResponseMessage response, Uri apiBaseUrl)
    {
        if (!response.Headers.TryGetValues(TokenExpirationHeader, out IEnumerable<string>? values))
        {
            return new TokenExpiry.Unavailable();
        }

        if (DateTimeOffset.TryParseExact(
                values.FirstOrDefault(),
                "yyyy-MM-dd HH:mm:ss 'UTC'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset expiresAt))
        {
            return new TokenExpiry.On(expiresAt);
        }

        logger.LogWarning(
            "GitHub token validation: the {Header} header is not a 'yyyy-MM-dd HH:mm:ss UTC' timestamp; expiry recorded as unavailable. Provider: github, Host: {Host}",
            TokenExpirationHeader,
            apiBaseUrl.Host);
        return new TokenExpiry.Unavailable();
    }

    private GitHubUserDto? DeserializeUserDto(string responseBody, Uri apiBaseUrl, int statusCode)
    {
        try
        {
            return JsonSerializer.Deserialize<GitHubUserDto>(responseBody, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "GitHub token validation: failed to parse response body. Provider: github, Host: {Host}, Status: {StatusCode}",
                apiBaseUrl.Host,
                statusCode);
            return null;
        }
    }

    private static string? SanitizeFilePath(string? path)
    {
        if (path is null)
        {
            return null;
        }

        if (path.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        if (path.StartsWith('/') || path.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        return path;
    }

    private void RecordRestHeadroom(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? remainingValues))
        {
            return;
        }

        if (!int.TryParse(remainingValues.FirstOrDefault(), out int remaining))
        {
            return;
        }

        int? limit = null;
        if (response.Headers.TryGetValues("X-RateLimit-Limit", out IEnumerable<string>? limitValues) &&
            int.TryParse(limitValues.FirstOrDefault(), out int parsedLimit))
        {
            limit = parsedLimit;
        }

        DateTimeOffset? resetAt = null;
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out IEnumerable<string>? resetValues) &&
            long.TryParse(resetValues.FirstOrDefault(), out long epochSeconds))
        {
            resetAt = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
        }

        rateBudget.Record(
            ProviderBudgetKey.GitHubRest,
            new RateBudgetReading(remaining, limit, resetAt, timeProvider.GetUtcNow()));
    }

    private static bool IsRateLimited(HttpResponseMessage response)
    {
        // Primary rate limit: X-RateLimit-Remaining == 0.
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? remaining) &&
            remaining.FirstOrDefault() == "0")
        {
            return true;
        }

        // Secondary rate limit (abuse detection): Retry-After must be a positive integer (seconds).
        // GitHub's documented secondary-rate-limit always sends a positive integer; a zero, negative,
        // or non-numeric value (e.g. injected by a CDN on a genuine permission 403) is not a valid
        // rate-limit signal and must not misclassify a real permission denial.
        if (response.Headers.TryGetValues("Retry-After", out IEnumerable<string>? retryAfterValues) &&
            int.TryParse(retryAfterValues.FirstOrDefault(), out int retryAfterSeconds) &&
            retryAfterSeconds > 0)
        {
            return true;
        }

        return false;
    }

    private static async Task<Error> ErrorFromBranchCreationFailureAsync(
        HttpResponseMessage response,
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden && IsRateLimited(response))
        {
            return GitHubErrors.RateLimitExhausted;
        }

        if (response.StatusCode != HttpStatusCode.Forbidden)
        {
            return ErrorFromNonSuccess(response);
        }

        if (response.Headers.TryGetValues("X-Accepted-GitHub-Permissions", out IEnumerable<string>? permValues))
        {
            string permissions = TruncateWithEllipsis(string.Join(", ", permValues), MaxPermissionsLength);
            string message = $"Branch pre-creation on {slug} returned 403 — token lacks {permissions}. " +
                $"Pending organization approval may also prevent access (fine-grained PATs require org approval).";
            return GitHubErrors.ProviderError(message);
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubErrorBodyDto? errorDto = DeserializeErrorBody(body);
        string rawBodyMessage = errorDto?.Message ?? body;
        string bodyMessage = TruncateWithEllipsis(RedactSecrets(rawBodyMessage), MaxBranchErrorBodyLength);
        return GitHubErrors.ProviderError($"Branch pre-creation on {slug} returned 403 — {bodyMessage}");
    }

    // Scoped to GitHub response bodies — no env-var pass (irrelevant here).
    private static string RedactSecrets(string input)
    {
        string result = HttpsUserinfoPattern().Replace(input, "https://***@");
        return KnownTokenPattern().Replace(result, "***");
    }

    private static string TruncateWithEllipsis(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), Ellipsis);

    private static Error ErrorFromNonSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden && IsRateLimited(response))
        {
            return GitHubErrors.RateLimitExhausted;
        }

        int statusCode = (int)response.StatusCode;
        Error error = GitHubErrors.UnexpectedStatusCode(statusCode);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return error with { Kind = ErrorKind.NotFound };
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return error with { Kind = ErrorKind.Unauthorized };
        }

        return error;
    }

    private static GitHubErrorBodyDto? DeserializeErrorBody(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<GitHubErrorBodyDto>(body, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private sealed record GitHubRepositoryInfoDto(string DefaultBranch);

    private sealed record GitHubBranchRuleDto(string Type);

    private sealed record GitHubUserDto(
        [property: JsonPropertyName("login")] string Login,
        [property: JsonPropertyName("username")] string? Username);

    private sealed record GitHubDependencyDto(
        int Number,
        string Title,
        string? State,
        GitHubRepositoryDto Repository);

    private sealed record GitHubRepositoryDto(string FullName);

    private sealed record GitHubRepositoryListItemDto(
        string FullName,
        bool Private,
        GitHubRepositoryPermissionsDto? Permissions);

    private sealed record GitHubRepoPermissionsResponseDto(GitHubRepositoryPermissionsDto? Permissions);

    private sealed record GitHubRepositoryPermissionsDto(bool Push);

    private sealed record GitHubGitRefDto(GitHubGitObjectDto? Object);

    private sealed record GitHubGitObjectDto(string Sha);

    private sealed record GitHubCompareDto(
        int AheadBy,
        IReadOnlyList<GitHubCommitRefDto> Commits);

    private sealed record GitHubCommitRefDto(string Sha);

    private sealed record GitHubRefDto(GitHubRefObjectDto? Object);

    private sealed record GitHubRefObjectDto(string? Sha);

    private sealed record GitHubPullRequestStateDto(
        string HtmlUrl,
        string State,
        string? MergedAt,
        DateTimeOffset UpdatedAt,
        GitHubPullRequestHeadDto? Head);

    private sealed record GitHubPullRequestHeadDto(string? Sha);

    private sealed record GitHubErrorBodyDto(string? Message);

    // GraphQL envelope records — deserialized with GraphQlJsonOptions (camelCase).
    // Data stays a JsonElement so its rateLimit block is read whatever shape the query's data record has.
    private sealed record GraphQlEnvelope(
        JsonElement? Data,
        IReadOnlyList<GraphQlError>? Errors);

    private sealed record GraphQlError(
        string Message,
        IReadOnlyList<GraphQlErrorLocation>? Locations,
        string? Type);

    private sealed record GraphQlErrorLocation(int Line, int Column);

    private sealed record GraphQlRateLimit(int Cost, int Remaining, int? Limit, DateTimeOffset? ResetAt);

    // GraphQL issue-list DTOs — deserialized with GraphQlJsonOptions (camelCase).
    private sealed record IssueListData(GraphQlRepository? Repository);

    private sealed record GraphQlRepository(
        GraphQlRef? DefaultBranchRef,
        GraphQlIssueConnection? Issues);

    private sealed record GraphQlRef(string Name);

    private sealed record GraphQlIssueConnection(
        GraphQlPageInfo PageInfo,
        IReadOnlyList<GraphQlIssueNode> Nodes);

    private sealed record GraphQlPageInfo(bool HasNextPage, string? EndCursor);

    private sealed record GraphQlIssueNode(
        int Number,
        string Title,
        string Url,
        GraphQlAuthor? Author,
        GraphQlLabelConnection Labels,
        GraphQlBlockedByConnection? BlockedBy);

    private sealed record GraphQlAuthor(string? Login);

    private sealed record GraphQlLabelConnection(IReadOnlyList<GraphQlLabelNode> Nodes);

    private sealed record GraphQlLabelNode(string Name);

    private sealed record GraphQlBlockedByConnection(
        int TotalCount,
        GraphQlPageInfo PageInfo,
        IReadOnlyList<GraphQlBlockerNode> Nodes);

    private sealed record GraphQlBlockerNode(
        int Number,
        string State,
        GraphQlBlockerRepo Repository);

    private sealed record GraphQlBlockerRepo(string NameWithOwner);

    // GraphQL blockedBy follow-up page DTOs — deserialized with GraphQlJsonOptions (camelCase).
    private sealed record BlockedByPageData(GraphQlBlockedByPageRepository? Repository);

    private sealed record GraphQlBlockedByPageRepository(GraphQlBlockedByPageIssue? Issue);

    private sealed record GraphQlBlockedByPageIssue(GraphQlBlockedByConnection? BlockedBy);

    // GraphQL IssueState query DTOs — deserialized with GraphQlJsonOptions (camelCase).
    private sealed record IssueStateData(IssueStateRepository? Repository);

    private sealed record IssueStateRepository(IssueStateNode? Issue);

    private sealed record IssueStateNode(string State);

    // GraphQL PrStatus query DTOs — deserialized with GraphQlJsonOptions (camelCase).
    private sealed record PrStatusData(PrStatusRepository? Repository);

    private sealed record PrStatusRepository(GraphQlPullRequestStatus? PullRequest);

    private sealed record GraphQlPullRequestStatus(string? Id, string State, bool Merged, string? HeadRefOid, bool IsDraft);

    // GraphQL PrReviewFeedback query DTOs — deserialized with GraphQlJsonOptions (camelCase).
    private sealed record PrReviewFeedbackData(PrReviewFeedbackRepository? Repository);

    private sealed record PrReviewFeedbackRepository(PrReviewFeedbackPullRequest? PullRequest);

    private sealed record PrReviewFeedbackPullRequest(
        GraphQlConversationCommentConnection? Comments,
        GraphQlReviewThreadConnection? ReviewThreads,
        GraphQlReviewConnection? Reviews);

    private sealed record GraphQlConversationCommentConnection(IReadOnlyList<GraphQlConversationCommentNode> Nodes);

    private sealed record GraphQlConversationCommentNode(
        long? DatabaseId,
        string Body,
        DateTimeOffset CreatedAt,
        GraphQlCommentAuthor? Author);

    private sealed record GraphQlCommentAuthor(
        string? Login,
        [property: JsonPropertyName("__typename")] string? Typename);

    private sealed record GraphQlReviewThreadConnection(IReadOnlyList<GraphQlReviewThreadNode> Nodes);

    private sealed record GraphQlReviewThreadNode(
        bool IsResolved,
        GraphQlReviewThreadCommentConnection? Comments);

    private sealed record GraphQlReviewThreadCommentConnection(IReadOnlyList<GraphQlReviewThreadCommentNode> Nodes);

    private sealed record GraphQlReviewThreadCommentNode(
        long? DatabaseId,
        string Body,
        string? Path,
        int? Line,
        int? OriginalLine,
        DateTimeOffset CreatedAt,
        GraphQlCommentAuthor? Author,
        GraphQlReviewReference? PullRequestReview);

    private sealed record GraphQlReviewReference(long? DatabaseId);

    private sealed record GraphQlReviewConnection(IReadOnlyList<GraphQlReviewNode> Nodes);

    private sealed record GraphQlReviewNode(
        long? DatabaseId,
        string? Body,
        DateTimeOffset? SubmittedAt,
        GraphQlCommentAuthor? Author);

    // GraphQL PrChecks query DTOs — deserialized with GraphQlJsonOptions (camelCase).
    private sealed record PrChecksData(PrChecksRepository? Repository);

    private sealed record PrChecksRepository(PrChecksPullRequest? PullRequest);

    private sealed record PrChecksPullRequest(PrChecksCommitConnection? Commits);

    private sealed record PrChecksCommitConnection(IReadOnlyList<PrChecksCommitEdge> Nodes);

    private sealed record PrChecksCommitEdge(PrChecksCommit? Commit);

    private sealed record PrChecksCommit(PrChecksStatusCheckRollup? StatusCheckRollup);

    private sealed record PrChecksStatusCheckRollup(
        string? State,
        PrChecksContextConnection? Contexts);

    private sealed record PrChecksContextConnection(IReadOnlyList<PrChecksContextUnionNode> Nodes);

    // Unified DTO covering both CheckRun and StatusContext inline-fragment shapes.
    // The __typename field tells us which variant to use; all other fields are nullable.
    // CheckRun fields: name, conclusion, detailsUrl.
    // StatusContext fields: context (as name), state (as conclusion), targetUrl (as detailsUrl).
    private sealed record PrChecksContextUnionNode(
        [property: JsonPropertyName("__typename")] string? Typename,
        // CheckRun fields
        string? Name,
        string? Conclusion,
        string? DetailsUrl,
        // StatusContext fields
        string? Context,
        string? State,
        string? TargetUrl);
}

internal sealed record BranchRules(bool RejectDirectPushes, bool RejectForcePushes, bool RejectDeletion);

internal static class GitHubErrors
{
    public static readonly Error InvalidBaseUrl = new(
        "GitHub.InvalidBaseUrl",
        "The base URL must use the https scheme.");

    public static readonly Error RateLimitExhausted = new(
        "GitHub.RateLimitExhausted",
        "GitHub API rate limit exhausted. Wait before retrying.");

    public static readonly Error InvalidPullRequestUrl = new(
        "GitHub.InvalidPullRequestUrl",
        "The pull request URL does not contain a valid PR number.");

    public static Error UnexpectedStatusCode(int statusCode) =>
        new("GitHub.UnexpectedStatusCode", $"GitHub API returned unexpected status code {statusCode}.");

    public static Error ProviderError(string message) =>
        new("GitHub.ProviderError", message);

    public const string GraphQlErrorCode = "GitHub.GraphQlError";

    public static Error GraphQlError(string message) =>
        new(GraphQlErrorCode, message);

    // Sentinel used internally by GetChecksAsync to signal 401/403 auth failure.
    // Never surfaced as a Result<T>.Fail to callers — mapped to Checks.Unreadable before returning.
    internal static readonly Error Unreadable = new(
        "GitHub.Unreadable",
        "GitHub returned 401 or 403 — check results could not be read.");
}
