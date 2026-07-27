using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal sealed partial class GitHubHttpClient(HttpClient httpClient) : IGitHubWriteProber
{
    private const string ApiVersion = "2026-03-10";
    private const string AllZerosSha = "0000000000000000000000000000000000000000";
    private const int MaxComments = 50;
    private const int MaxCommentBodyLength = 4000;
    private const string TruncatedSuffix = "[truncated]";
    private const int MaxRepositoryPages = 5;
    private const int RepositoriesPerPage = 100;
    private const int MaxBranchErrorBodyLength = 500;
    private const int MaxPermissionsLength = 200;
    private const string Ellipsis = "...";

    // Classic PATs remain accepted by design (issue #333 keeps them valid, just no longer advertised in the UI).
    // This constant is intentionally decoupled from RequiredScopes.For(github), which now carries fine-grained
    // permission display labels for the UI and is not a validation source for OAuth scope token checks.
    private static readonly IReadOnlyList<string> ClassicPatOAuthScopes = ["repo"];

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

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        string? accountName = DeserializeLogin(responseBody);

        if (!response.Headers.TryGetValues("X-OAuth-Scopes", out IEnumerable<string>? scopeValues))
        {
            return Result<TokenValidationResult>.Ok(TokenValidationResult.Validated([], accountName));
        }

        IReadOnlyList<string> missingScopes = ParseMissingScopes(scopeValues);

        return Result<TokenValidationResult>.Ok(TokenValidationResult.Validated(missingScopes, accountName));
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

    public async Task<Result<bool>> HasBranchCommitsAsync(
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
            return Result<bool>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubCompareDto? dto = JsonSerializer.Deserialize<GitHubCompareDto>(body, JsonOptions);

        return Result<bool>.Ok((dto?.AheadBy ?? 0) > 0);
    }

    public async Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string branchName,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<LatestBranchCommit>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string encodedBranch = Uri.EscapeDataString(branchName);
        string relativePath = $"repos/{owner}/{repo}/commits?sha={encodedBranch}&per_page=1";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<LatestBranchCommit>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitHubCommitListItemDto>? dtos =
            JsonSerializer.Deserialize<List<GitHubCommitListItemDto>>(body, JsonOptions);

        GitHubCommitListItemDto? latest = (dtos ?? []).FirstOrDefault();

        if (latest is null)
        {
            return Result<LatestBranchCommit>.Fail(GitHubErrors.NoBranchCommits);
        }

        string shortSha = latest.Sha.Length >= 7 ? latest.Sha[..7] : latest.Sha;
        string commitMessage = latest.Commit?.Message ?? string.Empty;
        int newlineIndex = commitMessage.IndexOf('\n', StringComparison.Ordinal);
        string firstLine = newlineIndex >= 0 ? commitMessage[..newlineIndex] : commitMessage;

        return Result<LatestBranchCommit>.Ok(new LatestBranchCommit(shortSha, firstLine));
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

    private static string? DeserializeLogin(string responseBody)
    {
        try
        {
            GitHubUserDto? dto = JsonSerializer.Deserialize<GitHubUserDto>(responseBody, JsonOptions);
            string? login = dto?.Login;
            return string.IsNullOrEmpty(login) ? null : login;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
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
        return response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? remaining) &&
            remaining.FirstOrDefault() == "0";
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

    private sealed record GitHubUserDto([property: JsonPropertyName("login")] string Login);

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

    private sealed record GitHubCompareDto(int AheadBy);

    private sealed record GitHubCommitListItemDto(string Sha, GitHubCommitDetailDto? Commit);

    private sealed record GitHubCommitDetailDto(string Message);

    private sealed record GitHubPullRequestStateDto(
        string HtmlUrl,
        string State,
        string? MergedAt,
        DateTimeOffset UpdatedAt);

    private sealed record GitHubErrorBodyDto(string? Message);
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

    public static readonly Error NoBranchCommits = new(
        "GitHub.NoBranchCommits",
        "The branch has no commits.");

    public static Error UnexpectedStatusCode(int statusCode) =>
        new("GitHub.UnexpectedStatusCode", $"GitHub API returned unexpected status code {statusCode}.");

    public static Error ProviderError(string message) =>
        new("GitHub.ProviderError", message);
}
