using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal sealed partial class GitHubHttpClient(
    HttpClient httpClient,
    ILogger<GitHubHttpClient> logger,
    DefaultBranchCache defaultBranchCache) : IGitHubWriteProber
{
    private const string ApiVersion = "2026-03-10";
    private const string AllZerosSha = "0000000000000000000000000000000000000000";
    private const string FineGrainedPatPrefix = "github_pat_";
    private const int MaxComments = 50;
    private const int MaxCommentBodyLength = 4000;
    private const string TruncatedSuffix = "[truncated]";
    private const int MaxRepositoryPages = 5;
    private const int RepositoriesPerPage = 100;
    private const int MaxIssuePages = 20;
    private const int IssuesPerPage = 100;
    private const int MaxBranchErrorBodyLength = 500;
    private const int MaxPermissionsLength = 200;
    private const string Ellipsis = "...";

    private const string IssueListQuery = """
        query IssueList($owner: String!, $name: String!, $issuesAfter: String) {
          rateLimit { cost remaining limit resetAt }
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
                body
                url
                state
                author { login }
                labels(first: 50) { nodes { name } }
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

    private const string BlockedByPageQuery = """
        query BlockedByPage($owner: String!, $name: String!, $number: Int!, $blockedByAfter: String) {
          rateLimit { cost remaining }
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

    // Classic PATs remain accepted by design (issue #333 keeps them valid, just no longer advertised in the UI).
    // This constant is intentionally decoupled from RequiredScopes.For(github), which now carries fine-grained
    // permission display labels for the UI and is not a validation source for OAuth scope token checks.
    private static readonly IReadOnlyList<string> ClassicPatOAuthScopes = ["repo"];

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
            return Result<BranchRules>.Ok(new BranchRules(false, false, false));
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<BranchRules>.Fail(ErrorFromNonSuccess(response));
        }

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
        CancellationToken cancellationToken)
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
            return Result<TData>.Fail(ErrorFromNonSuccess(response));
        }

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        GraphQlEnvelope<TData>? envelope =
            JsonSerializer.Deserialize<GraphQlEnvelope<TData>>(responseBody, GraphQlJsonOptions);

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
            return Result<TData>.Fail(GitHubErrors.GraphQlError(safeMessage));
        }

        if (envelope?.Data is null)
        {
            return Result<TData>.Fail(
                GitHubErrors.ProviderError("GraphQL response contained neither data nor errors."));
        }

        if (envelope.RateLimit is { } rateLimit)
        {
            logger.LogDebug(
                "GitHub GraphQL cost={Cost} remaining={Remaining}",
                rateLimit.Cost,
                rateLimit.Remaining);
        }

        return Result<TData>.Ok(envelope.Data);
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
                    Body: node.Body ?? string.Empty,
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

            while (true)
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

    public async Task<Result<IssueListing>> GetIssuesAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IssueListing>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        List<ProviderIssue> allIssues = [];

        for (int page = 1; page <= MaxIssuePages; page++)
        {
            string relativePath = $"repos/{owner}/{repo}/issues?labels=foundry&state=open&per_page={IssuesPerPage}&page={page}";
            Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

            using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result<IssueListing>.Fail(ErrorFromNonSuccess(response));
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            List<GitHubIssueDto>? dtos = JsonSerializer.Deserialize<List<GitHubIssueDto>>(body, JsonOptions);

            List<GitHubIssueDto> pageItems = dtos ?? [];
            foreach (GitHubIssueDto dto in pageItems)
            {
                IReadOnlyList<string> labels = dto.Labels
                    .Select(l => l.Name)
                    .ToList();
                allIssues.Add(new ProviderIssue(
                    Number: dto.Number,
                    Title: dto.Title,
                    Body: dto.Body ?? string.Empty,
                    Author: dto.User.Login,
                    Url: dto.HtmlUrl,
                    Labels: labels,
                    IssueKindLabel: LabelClassifier.ClassifyKind(labels)));
            }

            if (pageItems.Count < IssuesPerPage)
            {
                return Result<IssueListing>.Ok(new IssueListing(allIssues, IsComplete: true));
            }
        }

        return Result<IssueListing>.Ok(new IssueListing(allIssues, IsComplete: false));
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

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string relativePath = $"repos/{owner}/{repo}/issues/{issueNumber}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<bool>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubIssueStateDto? dto = JsonSerializer.Deserialize<GitHubIssueStateDto>(body, JsonOptions);

        bool isClosed = string.Equals(dto?.State, "closed", StringComparison.OrdinalIgnoreCase);
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

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string relativePath = $"repos/{owner}/{repo}/pulls/{prNumber}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<PullRequestStatus>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubPullRequestDto? dto = JsonSerializer.Deserialize<GitHubPullRequestDto>(body, JsonOptions);

        bool isClosed = string.Equals(dto?.State, "closed", StringComparison.OrdinalIgnoreCase);
        bool isMerged = dto?.MergedAt is not null;
        return Result<PullRequestStatus>.Ok(new PullRequestStatus(isClosed, isMerged));
    }

    public async Task<Result<ReviewFeedback>> GetPullRequestReviewFeedbackAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        DateTimeOffset since,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<ReviewFeedback>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        if (!TryParsePrNumber(pullRequestUrl, out int prNumber))
        {
            return Result<ReviewFeedback>.Fail(GitHubErrors.InvalidPullRequestUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);

        Result<IReadOnlyList<GitHubPullRequestReviewDto>> reviewsResult = await FetchPullRequestReviewsAsync(
            apiBaseUrl, owner, repo, prNumber, token, cancellationToken);

        if (reviewsResult is not Result<IReadOnlyList<GitHubPullRequestReviewDto>>.Success reviewsSuccess)
        {
            Error error = ((Result<IReadOnlyList<GitHubPullRequestReviewDto>>.Failure)reviewsResult).Error;
            return Result<ReviewFeedback>.Fail(error);
        }

        IReadOnlyList<GitHubPullRequestReviewDto> changesRequestedReviews = reviewsSuccess.Value
            .Where(r => string.Equals(r.State, "CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase))
            .Where(r => r.SubmittedAt > since)
            .ToList();

        List<ReviewComment> comments = [];
        foreach (GitHubPullRequestReviewDto review in changesRequestedReviews)
        {
            if (comments.Count >= MaxComments)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(review.Body))
            {
                comments.Add(new ReviewComment(TruncateBody(review.Body)));
            }

            if (comments.Count >= MaxComments)
            {
                break;
            }

            Result<IReadOnlyList<GitHubPullRequestReviewCommentDto>> fileCommentsResult =
                await FetchPullRequestReviewCommentsAsync(
                    apiBaseUrl, owner, repo, prNumber, review.Id, token, cancellationToken);

            if (fileCommentsResult is not Result<IReadOnlyList<GitHubPullRequestReviewCommentDto>>.Success fileCommentsSuccess)
            {
                continue;
            }

            foreach (GitHubPullRequestReviewCommentDto fileComment in fileCommentsSuccess.Value)
            {
                if (comments.Count >= MaxComments)
                {
                    break;
                }

                int? line = fileComment.Line ?? fileComment.OriginalLine;
                string? sanitizedPath = SanitizeFilePath(fileComment.Path);
                comments.Add(new ReviewComment(TruncateBody(fileComment.Body), sanitizedPath, line));
            }
        }

        return Result<ReviewFeedback>.Ok(new ReviewFeedback(comments));
    }

    private async Task<Result<IReadOnlyList<GitHubPullRequestReviewDto>>> FetchPullRequestReviewsAsync(
        Uri apiBaseUrl,
        string owner,
        string repo,
        int prNumber,
        string token,
        CancellationToken cancellationToken)
    {
        string relativePath = $"repos/{owner}/{repo}/pulls/{prNumber}/reviews";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<GitHubPullRequestReviewDto>>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitHubPullRequestReviewDto>? dtos =
            JsonSerializer.Deserialize<List<GitHubPullRequestReviewDto>>(body, JsonOptions);

        return Result<IReadOnlyList<GitHubPullRequestReviewDto>>.Ok(dtos ?? []);
    }

    private async Task<Result<IReadOnlyList<GitHubPullRequestReviewCommentDto>>> FetchPullRequestReviewCommentsAsync(
        Uri apiBaseUrl,
        string owner,
        string repo,
        int prNumber,
        long reviewId,
        string token,
        CancellationToken cancellationToken)
    {
        string relativePath = $"repos/{owner}/{repo}/pulls/{prNumber}/reviews/{reviewId}/comments";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<GitHubPullRequestReviewCommentDto>>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitHubPullRequestReviewCommentDto>? dtos =
            JsonSerializer.Deserialize<List<GitHubPullRequestReviewCommentDto>>(body, JsonOptions);

        return Result<IReadOnlyList<GitHubPullRequestReviewCommentDto>>.Ok(dtos ?? []);
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

        if (!response.Headers.TryGetValues("X-OAuth-Scopes", out IEnumerable<string>? scopeValues))
        {
            return IsFineGrainedPat(token)
                ? Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.Authenticated(login))
                : Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.ScopesUnverifiable(login));
        }

        IReadOnlyList<string> missingScopes = ParseMissingScopes(scopeValues);

        return Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.Authenticated(login, missingScopes));
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

        string body = JsonSerializer.Serialize(
            new { @ref = $"refs/heads/{probeName}", sha = AllZerosSha },
            JsonOptions);

        using HttpRequestMessage request = new(HttpMethod.Post, requestUri);
        AddGitHubHeaders(request, token);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

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

        return ClassifyProbeResponse(response, WritePermission.PullRequests);
    }

    public async Task<Result<WritePermissionProbeResult>> ProbeWriteAccessAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        Result<WritePermissionProbeResult> contentsResult = await ProbeContentsWriteAsync(
            apiBaseUrl, slug, token, cancellationToken);

        if (!IsGranted(contentsResult))
        {
            return contentsResult;
        }

        Result<WritePermissionProbeResult> issuesResult = await ProbeIssuesWriteAsync(
            apiBaseUrl, slug, token, cancellationToken);

        if (!IsGranted(issuesResult))
        {
            return issuesResult;
        }

        Result<WritePermissionProbeResult> pullsResult = await ProbePullRequestsWriteAsync(
            apiBaseUrl, slug, token, cancellationToken);

        if (!IsGranted(pullsResult))
        {
            return pullsResult;
        }

        return Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Granted());
    }

    private static bool IsGranted(Result<WritePermissionProbeResult> result) =>
        result is Result<WritePermissionProbeResult>.Success { Value: WritePermissionProbeResult.Granted };

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
            return Result<bool>.Ok(false);
        }

        if (!createRefResponse.IsSuccessStatusCode)
        {
            Error branchError = await ErrorFromBranchCreationFailureAsync(createRefResponse, slug, cancellationToken);
            return Result<bool>.Fail(branchError);
        }

        return Result<bool>.Ok(true);
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

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitHubPullRequestStateDto>? dtos =
            JsonSerializer.Deserialize<List<GitHubPullRequestStateDto>>(body, JsonOptions);

        List<GitHubPullRequestStateDto> items = dtos ?? [];

        if (items.Count == 0)
        {
            return Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null));
        }

        GitHubPullRequestStateDto? merged = items.FirstOrDefault(dto => dto.MergedAt is not null);

        GitHubPullRequestStateDto selected = merged
            ?? items.MaxBy(dto => dto.UpdatedAt)!;

        MergeRequestPresence presence = selected.MergedAt is not null
            ? MergeRequestPresence.Merged
            : string.Equals(selected.State, "open", StringComparison.OrdinalIgnoreCase)
                ? MergeRequestPresence.Open
                : MergeRequestPresence.Closed;

        return Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(presence, selected.HtmlUrl));
    }

    private static Result<WritePermissionProbeResult> ClassifyProbeResponse(
        HttpResponseMessage response,
        WritePermission permission)
    {
        return response.StatusCode switch
        {
            HttpStatusCode.UnprocessableEntity => Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Granted()),
            HttpStatusCode.NotFound => Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Granted()),
            // A 403 carrying rate-limit headers is a transient exhaustion, not a permission denial.
            // Returning Fail here prevents the caller from recording a spurious Denied verdict.
            HttpStatusCode.Forbidden when IsRateLimited(response) =>
                Result<WritePermissionProbeResult>.Fail(GitHubErrors.RateLimitExhausted),
            HttpStatusCode.Forbidden => Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Missing(permission)),
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

    private static bool IsFineGrainedPat(string token) =>
        token.StartsWith(FineGrainedPatPrefix, StringComparison.Ordinal);

    private static List<string> ParseMissingScopes(IEnumerable<string> scopeHeaders)
    {
        HashSet<string> grantedScopes = new(StringComparer.OrdinalIgnoreCase);
        foreach (string header in scopeHeaders)
        {
            foreach (string scope in header.Split(','))
            {
                grantedScopes.Add(scope.Trim());
            }
        }

        List<string> missing = [];
        foreach (string required in ClassicPatOAuthScopes)
        {
            if (!grantedScopes.Contains(required))
            {
                missing.Add(required);
            }
        }

        return missing;
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

    private static string TruncateBody(string body)
    {
        if (body.Length <= MaxCommentBodyLength)
        {
            return body;
        }

        return string.Concat(body.AsSpan(0, MaxCommentBodyLength), TruncatedSuffix);
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

    private sealed record GitHubIssueDto(
        int Number,
        string Title,
        string? Body,
        GitHubUserDto User,
        string HtmlUrl,
        IReadOnlyList<GitHubLabelDto> Labels);

    private sealed record GitHubUserDto(
        [property: JsonPropertyName("login")] string Login,
        [property: JsonPropertyName("username")] string? Username);

    private sealed record GitHubLabelDto(string Name);

    private sealed record GitHubDependencyDto(
        int Number,
        string Title,
        string? State,
        GitHubRepositoryDto Repository);

    private sealed record GitHubRepositoryDto(string FullName);

    private sealed record GitHubIssueStateDto(string State);

    private sealed record GitHubPullRequestDto(string State, string? MergedAt);

    private sealed record GitHubPullRequestReviewDto(
        long Id,
        string State,
        string Body,
        DateTimeOffset SubmittedAt);

    private sealed record GitHubPullRequestReviewCommentDto(
        string Body,
        string Path,
        int? Line,
        int? OriginalLine);

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

    private sealed record GitHubPullRequestStateDto(
        string HtmlUrl,
        string State,
        string? MergedAt,
        DateTimeOffset UpdatedAt);

    private sealed record GitHubErrorBodyDto(string? Message);

    // GraphQL envelope records — deserialized with GraphQlJsonOptions (camelCase).
    // TData is unconstrained here so Data can be null when the GraphQL response omits or nulls it.
    private sealed record GraphQlEnvelope<TData>(
        TData? Data,
        IReadOnlyList<GraphQlError>? Errors,
        GraphQlRateLimit? RateLimit)
        where TData : class;

    private sealed record GraphQlError(
        string Message,
        IReadOnlyList<GraphQlErrorLocation>? Locations,
        string? Type);

    private sealed record GraphQlErrorLocation(int Line, int Column);

    private sealed record GraphQlRateLimit(int Cost, int Remaining);

    // GraphQL issue-list DTOs — deserialized with GraphQlJsonOptions (camelCase).
    private sealed record IssueListData(GraphQlRateLimit? RateLimit, GraphQlRepository? Repository);

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
        string? Body,
        string Url,
        string State,
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
    private sealed record BlockedByPageData(GraphQlRateLimit? RateLimit, GraphQlBlockedByPageRepository? Repository);

    private sealed record GraphQlBlockedByPageRepository(GraphQlBlockedByPageIssue? Issue);

    private sealed record GraphQlBlockedByPageIssue(GraphQlBlockedByConnection? BlockedBy);
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

    public static Error GraphQlError(string message) =>
        new("GitHub.GraphQlError", message);
}
