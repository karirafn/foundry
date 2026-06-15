using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure;

internal sealed partial class GitHubHttpClient(HttpClient httpClient)
{
    private const string ApiVersion = "2026-03-10";
    private const int MaxComments = 50;
    private const int MaxCommentBodyLength = 4000;
    private const string TruncatedSuffix = "[truncated]";
    private const int MaxRepositoryPages = 5;
    private const int RepositoriesPerPage = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<Result<string>> GetDefaultBranchAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<string>.Fail(GitHubErrors.InvalidBaseUrl);
        }

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

    public async Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<ProviderIssue>>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string relativePath = $"repos/{Uri.EscapeDataString(slug.Owner)}/{Uri.EscapeDataString(slug.Name)}/issues?labels=foundry&state=open";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<ProviderIssue>>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitHubIssueDto>? dtos = JsonSerializer.Deserialize<List<GitHubIssueDto>>(body, JsonOptions);

        IReadOnlyList<ProviderIssue> issues = (dtos ?? [])
            .Select(dto =>
            {
                IReadOnlyList<string> labels = dto.Labels
                    .Select(l => l.Name)
                    .ToList();
                return new ProviderIssue(
                    Number: dto.Number,
                    Title: dto.Title,
                    Body: dto.Body ?? string.Empty,
                    Author: dto.User.Login,
                    Url: dto.HtmlUrl,
                    Labels: labels,
                    IssueKindLabel: LabelClassifier.ClassifyKind(labels));
            })
            .ToList();

        return Result<IReadOnlyList<ProviderIssue>>.Ok(issues);
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

    public async Task<Result<TokenValidationResult>> ValidateTokenAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<TokenValidationResult>.Fail(GitHubErrors.InvalidBaseUrl);
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
            return Result<TokenValidationResult>.Ok(TokenValidationResult.AuthFailure());
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<TokenValidationResult>.Fail(ErrorFromNonSuccess(response));
        }

        if (!response.Headers.TryGetValues("X-OAuth-Scopes", out IEnumerable<string>? scopeValues))
        {
            return Result<TokenValidationResult>.Ok(TokenValidationResult.Validated([]));
        }

        IReadOnlyList<string> missingScopes = ParseMissingScopes(scopeValues);

        return Result<TokenValidationResult>.Ok(TokenValidationResult.Validated(missingScopes));
    }

    public async Task<Result<IReadOnlyList<AvailableRepository>>> ListRepositoriesAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<AvailableRepository>>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        List<AvailableRepository> repositories = [];

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
                return Result<IReadOnlyList<AvailableRepository>>.Fail(ErrorFromNonSuccess(response));
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            List<GitHubRepositoryListItemDto>? dtos =
                JsonSerializer.Deserialize<List<GitHubRepositoryListItemDto>>(body, JsonOptions);

            List<GitHubRepositoryListItemDto> pageItems = dtos ?? [];
            foreach (GitHubRepositoryListItemDto dto in pageItems)
            {
                repositories.Add(new AvailableRepository(dto.FullName, dto.Private));
            }

            if (pageItems.Count < RepositoriesPerPage)
            {
                break;
            }
        }

        return Result<IReadOnlyList<AvailableRepository>>.Ok(repositories);
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        string uriString = uri.ToString();
        return uriString.EndsWith('/') ? uri : new Uri(uriString + '/');
    }

    private static List<string> ParseMissingScopes(IEnumerable<string> scopeHeaders)
    {
        HashSet<string> grantedScopes = [];
        foreach (string header in scopeHeaders)
        {
            foreach (string scope in header.Split(','))
            {
                grantedScopes.Add(scope.Trim());
            }
        }

        List<string> missing = [];
        if (!grantedScopes.Contains("repo"))
        {
            missing.Add("repo");
        }

        return missing;
    }

    [GeneratedRegex(@"/pull/(\d+)(?:[/?#]|$)")]
    private static partial Regex PrNumberRegex();

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

    private static Error ErrorFromNonSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? remaining) &&
                remaining.FirstOrDefault() == "0")
            {
                return GitHubErrors.RateLimitExhausted;
            }
        }

        int statusCode = (int)response.StatusCode;
        return GitHubErrors.UnexpectedStatusCode(statusCode);
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

    private sealed record GitHubUserDto(string Login);

    private sealed record GitHubLabelDto(string Name);

    private sealed record GitHubDependencyDto(
        int Number,
        string Title,
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

    private sealed record GitHubRepositoryListItemDto(string FullName, bool Private);
}

internal sealed record BranchRules(bool RejectDirectPushes, bool RejectForcePushes, bool RejectDeletion);

internal sealed class TokenValidationResult
{
    private TokenValidationResult(bool isValid, bool isAuthFailure, IReadOnlyList<string> missingScopes)
    {
        IsValid = isValid;
        IsAuthFailure = isAuthFailure;
        MissingScopes = missingScopes;
    }

    public bool IsValid { get; }

    public bool IsAuthFailure { get; }

    public IReadOnlyList<string> MissingScopes { get; }

    public static TokenValidationResult Validated(IReadOnlyList<string> missingScopes) =>
        new(missingScopes.Count == 0, false, missingScopes);

    public static TokenValidationResult AuthFailure() =>
        new(false, true, []);
}

internal sealed record AvailableRepository(string Slug, bool IsPrivate);

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
}
