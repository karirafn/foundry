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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
        Uri baseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (baseUrl.Scheme is not ("https" or "http"))
        {
            return Result<IReadOnlyList<ProviderIssue>>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string relativePath = $"repos/{Uri.EscapeDataString(slug.Owner)}/{Uri.EscapeDataString(slug.Name)}/issues?labels=foundry&state=open";
        Uri requestUri = new(baseUrl, relativePath);

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
            .Select(dto => new ProviderIssue(
                Number: dto.Number,
                Title: dto.Title,
                Body: dto.Body ?? string.Empty,
                Author: dto.User.Login,
                Url: dto.HtmlUrl,
                Labels: dto.Labels
                    .Select(l => l.Name)
                    .ToList()))
            .ToList();

        return Result<IReadOnlyList<ProviderIssue>>.Ok(issues);
    }

    public async Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
        Uri baseUrl,
        RepositorySlug slug,
        int issueNumber,
        string token,
        CancellationToken cancellationToken)
    {
        if (baseUrl.Scheme is not ("https" or "http"))
        {
            return Result<IReadOnlyList<int>>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string relativePath = $"repos/{owner}/{repo}/issues/{issueNumber}/dependencies/blocked_by";
        Uri requestUri = new(baseUrl, relativePath);

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
        Uri baseUrl,
        RepositorySlug slug,
        int issueNumber,
        string token,
        CancellationToken cancellationToken)
    {
        if (baseUrl.Scheme is not ("https" or "http"))
        {
            return Result<bool>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string relativePath = $"repos/{owner}/{repo}/issues/{issueNumber}";
        Uri requestUri = new(baseUrl, relativePath);

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
        Uri baseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (baseUrl.Scheme is not ("https" or "http"))
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
        Uri requestUri = new(baseUrl, relativePath);

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
        Uri baseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        DateTimeOffset since,
        string token,
        CancellationToken cancellationToken)
    {
        if (baseUrl.Scheme is not ("https" or "http"))
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
            baseUrl, owner, repo, prNumber, token, cancellationToken);

        if (reviewsResult is not Result<IReadOnlyList<GitHubPullRequestReviewDto>>.Success reviewsSuccess)
        {
            return Result<ReviewFeedback>.Fail(((Result<IReadOnlyList<GitHubPullRequestReviewDto>>.Failure)reviewsResult).Error);
        }

        IReadOnlyList<GitHubPullRequestReviewDto> changesRequestedReviews = reviewsSuccess.Value
            .Where(r => string.Equals(r.State, "CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase))
            .Where(r => r.SubmittedAt > since)
            .ToList();

        List<ReviewComment> comments = [];
        foreach (GitHubPullRequestReviewDto review in changesRequestedReviews)
        {
            if (!string.IsNullOrWhiteSpace(review.Body))
            {
                comments.Add(new ReviewComment(review.Body));
            }

            Result<IReadOnlyList<GitHubPullRequestReviewCommentDto>> fileCommentsResult =
                await FetchPullRequestReviewCommentsAsync(
                    baseUrl, owner, repo, prNumber, review.Id, token, cancellationToken);

            if (fileCommentsResult is not Result<IReadOnlyList<GitHubPullRequestReviewCommentDto>>.Success fileCommentsSuccess)
            {
                continue;
            }

            foreach (GitHubPullRequestReviewCommentDto fileComment in fileCommentsSuccess.Value)
            {
                int? line = fileComment.Line ?? fileComment.OriginalLine;
                comments.Add(new ReviewComment(fileComment.Body, fileComment.Path, line));
            }
        }

        return Result<ReviewFeedback>.Ok(new ReviewFeedback(comments));
    }

    private async Task<Result<IReadOnlyList<GitHubPullRequestReviewDto>>> FetchPullRequestReviewsAsync(
        Uri baseUrl,
        string owner,
        string repo,
        int prNumber,
        string token,
        CancellationToken cancellationToken)
    {
        string relativePath = $"repos/{owner}/{repo}/pulls/{prNumber}/reviews";
        Uri requestUri = new(baseUrl, relativePath);

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
        Uri baseUrl,
        string owner,
        string repo,
        int prNumber,
        long reviewId,
        string token,
        CancellationToken cancellationToken)
    {
        string relativePath = $"repos/{owner}/{repo}/pulls/{prNumber}/reviews/{reviewId}/comments";
        Uri requestUri = new(baseUrl, relativePath);

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
}

internal static class GitHubErrors
{
    public static readonly Error InvalidBaseUrl = new(
        "GitHub.InvalidBaseUrl",
        "The base URL must use the https or http scheme.");

    public static readonly Error RateLimitExhausted = new(
        "GitHub.RateLimitExhausted",
        "GitHub API rate limit exhausted. Wait before retrying.");

    public static readonly Error InvalidPullRequestUrl = new(
        "GitHub.InvalidPullRequestUrl",
        "The pull request URL does not contain a valid PR number.");

    public static Error UnexpectedStatusCode(int statusCode) =>
        new("GitHub.UnexpectedStatusCode", $"GitHub API returned unexpected status code {statusCode}.");
}
