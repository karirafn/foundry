using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure;

internal sealed partial class GitLabHttpClient(HttpClient httpClient)
{
    private const int MaxCommentBodyLength = 4000;
    private const string TruncatedSuffix = "[truncated]";
    private const int MaxRepositoryPages = 5;
    private const int RepositoriesPerPage = 100;
    private const int MaxReviewComments = 50;
    private const int MaxFilePathLength = 4096; // PATH_MAX

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<Result<TokenValidationResult>> ValidateTokenAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<TokenValidationResult>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), "user");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Add("PRIVATE-TOKEN", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            return Result<TokenValidationResult>.Ok(TokenValidationResult.AuthFailure());
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<TokenValidationResult>.Fail(GitLabErrors.UnexpectedStatusCode((int)response.StatusCode));
        }

        return Result<TokenValidationResult>.Ok(TokenValidationResult.Validated([]));
    }

    public async Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<ProviderIssue>>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string relativePath = $"projects/{encodedPath}/issues?labels=foundry&state=opened";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<ProviderIssue>>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitLabIssueDto>? dtos = JsonSerializer.Deserialize<List<GitLabIssueDto>>(body, JsonOptions);

        IReadOnlyList<ProviderIssue> issues = (dtos ?? [])
            .Select(dto => new ProviderIssue(
                Number: dto.Iid,
                Title: dto.Title,
                Body: dto.Description ?? string.Empty,
                Author: dto.Author.Username,
                Url: dto.WebUrl,
                Labels: dto.Labels,
                IssueKindLabel: LabelClassifier.ClassifyKind(dto.Labels)))
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
            return Result<IReadOnlyList<int>>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string relativePath = $"projects/{encodedPath}/issues/{issueNumber}/links";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return Result<IReadOnlyList<int>>.Ok([]);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<int>>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitLabIssueLinkDto>? dtos = JsonSerializer.Deserialize<List<GitLabIssueLinkDto>>(body, JsonOptions);

        IReadOnlyList<int> issueNumbers = (dtos ?? [])
            .Where(dto => string.Equals(dto.LinkType, "is_blocked_by", StringComparison.OrdinalIgnoreCase))
            .Where(dto => !string.Equals(dto.State, "closed", StringComparison.OrdinalIgnoreCase))
            .Select(dto => dto.Iid)
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
            return Result<bool>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string relativePath = $"projects/{encodedPath}/issues/{issueNumber}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<bool>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabIssueStateDto? dto = JsonSerializer.Deserialize<GitLabIssueStateDto>(body, JsonOptions);

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
            return Result<PullRequestStatus>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        if (!TryParseMrIid(pullRequestUrl, out int mrIid))
        {
            return Result<PullRequestStatus>.Fail(GitLabErrors.InvalidMergeRequestUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string relativePath = $"projects/{encodedPath}/merge_requests/{mrIid}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<PullRequestStatus>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabMergeRequestDto? dto = JsonSerializer.Deserialize<GitLabMergeRequestDto>(body, JsonOptions);

        bool isMerged = string.Equals(dto?.State, "merged", StringComparison.OrdinalIgnoreCase);
        bool isClosed = isMerged ||
            string.Equals(dto?.State, "closed", StringComparison.OrdinalIgnoreCase);

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
            return Result<ReviewFeedback>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        if (!TryParseMrIid(pullRequestUrl, out int mrIid))
        {
            return Result<ReviewFeedback>.Fail(GitLabErrors.InvalidMergeRequestUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string relativePath = $"projects/{encodedPath}/merge_requests/{mrIid}/discussions";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<ReviewFeedback>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitLabDiscussionDto>? dtos = JsonSerializer.Deserialize<List<GitLabDiscussionDto>>(body, JsonOptions);

        List<ReviewComment> comments = [];

        foreach (GitLabDiscussionDto discussion in dtos ?? [])
        {
            if (comments.Count >= MaxReviewComments)
            {
                break;
            }

            if (discussion.Notes.Count == 0)
            {
                continue;
            }

            GitLabNoteDto firstNote = discussion.Notes[0];
            if (!firstNote.Resolvable || firstNote.Resolved)
            {
                continue;
            }

            if (firstNote.UpdatedAt <= since)
            {
                continue;
            }

            string? sanitizedPath = SanitizeFilePath(firstNote.Position?.NewPath);
            comments.Add(new ReviewComment(TruncateBody(firstNote.Body), sanitizedPath));
        }

        return Result<ReviewFeedback>.Ok(new ReviewFeedback(comments));
    }

    public async Task<Result<IReadOnlyList<AvailableRepository>>> ListRepositoriesAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<AvailableRepository>>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        List<AvailableRepository> repositories = [];

        for (int page = 1; page <= MaxRepositoryPages; page++)
        {
            string relativePath = $"projects?membership=true&simple=true&per_page={RepositoriesPerPage}&page={page}";
            Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

            using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            AddCommonHeaders(request, token);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result<IReadOnlyList<AvailableRepository>>.Fail(ErrorFromNonSuccess(response));
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            List<GitLabProjectListItemDto>? dtos =
                JsonSerializer.Deserialize<List<GitLabProjectListItemDto>>(body, JsonOptions);

            List<GitLabProjectListItemDto> pageItems = dtos ?? [];
            foreach (GitLabProjectListItemDto dto in pageItems)
            {
                bool isPrivate = string.Equals(dto.Visibility, "private", StringComparison.OrdinalIgnoreCase);
                repositories.Add(new AvailableRepository(dto.PathWithNamespace, isPrivate));
            }

            if (pageItems.Count < RepositoriesPerPage)
            {
                break;
            }
        }

        return Result<IReadOnlyList<AvailableRepository>>.Ok(repositories);
    }

    public async Task<Result<bool>> CreateBranchAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string branchName,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<bool>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);

        Result<string> defaultBranchResult = await GetDefaultBranchAsync(
            apiBaseUrl, slug, token, cancellationToken);

        if (defaultBranchResult is not Result<string>.Success defaultBranchSuccess)
        {
            Error error = ((Result<string>.Failure)defaultBranchResult).Error;
            return Result<bool>.Fail(error);
        }

        string defaultBranch = defaultBranchSuccess.Value;
        string createBranchPath = $"projects/{encodedPath}/repository/branches";
        Uri createBranchUri = new(EnsureTrailingSlash(apiBaseUrl), createBranchPath);

        string createBody = JsonSerializer.Serialize(
            new { branch = branchName, @ref = defaultBranch },
            JsonOptions);

        using HttpRequestMessage createRequest = new(HttpMethod.Post, createBranchUri);
        AddCommonHeaders(createRequest, token);
        createRequest.Content = new StringContent(createBody, Encoding.UTF8, "application/json");

        using HttpResponseMessage createResponse = await httpClient.SendAsync(createRequest, cancellationToken);

        if (createResponse.StatusCode == HttpStatusCode.BadRequest)
        {
            return Result<bool>.Ok(false);
        }

        if (!createResponse.IsSuccessStatusCode)
        {
            return Result<bool>.Fail(ErrorFromNonSuccess(createResponse));
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
            return Result<bool>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string encodedDefault = Uri.EscapeDataString(defaultBranch);
        string encodedBranch = Uri.EscapeDataString(branchName);
        string relativePath = $"projects/{encodedPath}/repository/compare?from={encodedDefault}&to={encodedBranch}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<bool>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabCompareDto? dto = JsonSerializer.Deserialize<GitLabCompareDto>(body, JsonOptions);

        return Result<bool>.Ok((dto?.Commits ?? []).Count > 0);
    }

    public async Task<Result<string>> GetPullRequestByBranchAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string branchName,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<string>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string encodedBranch = Uri.EscapeDataString(branchName);
        string relativePath = $"projects/{encodedPath}/merge_requests?source_branch={encodedBranch}&state=opened";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<string>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitLabMergeRequestListItemDto>? dtos =
            JsonSerializer.Deserialize<List<GitLabMergeRequestListItemDto>>(body, JsonOptions);

        string mergeRequestUrl = (dtos ?? []).FirstOrDefault()?.WebUrl ?? string.Empty;
        return Result<string>.Ok(mergeRequestUrl);
    }

    public async Task<Result<string>> GetDefaultBranchAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<string>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string relativePath = $"projects/{encodedPath}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<string>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabProjectInfoDto? dto = JsonSerializer.Deserialize<GitLabProjectInfoDto>(body, JsonOptions);

        return Result<string>.Ok(dto?.DefaultBranch ?? string.Empty);
    }

    public async Task<Result<BranchRules>> GetBranchProtectionAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string branch,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<BranchRules>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string encodedBranch = Uri.EscapeDataString(branch);
        string relativePath = $"projects/{encodedPath}/protected_branches/{encodedBranch}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

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
        GitLabProtectedBranchDto? dto = JsonSerializer.Deserialize<GitLabProtectedBranchDto>(body, JsonOptions);

        bool rejectDirectPushes = dto?.AllowForcePush is false &&
            (dto.PushAccessLevels ?? []).All(a => a.AccessLevel == 0);
        bool rejectForcePushes = dto?.AllowForcePush is false;
        bool rejectDeletion = !(dto?.AllowForcePush ?? true);

        return Result<BranchRules>.Ok(new BranchRules(rejectDirectPushes, rejectForcePushes, rejectDeletion));
    }

    private static void AddCommonHeaders(HttpRequestMessage request, string token)
    {
        request.Headers.Add("PRIVATE-TOKEN", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        string uriString = uri.ToString();
        return uriString.EndsWith('/') ? uri : new Uri(uriString + '/');
    }

    [GeneratedRegex(@"/merge_requests/(\d+)(?:[/?#]|$)")]
    private static partial Regex MrIidRegex();

    private static bool TryParseMrIid(string mergeRequestUrl, out int mrIid)
    {
        Match match = MrIidRegex().Match(mergeRequestUrl);
        if (match.Success && int.TryParse(match.Groups[1].Value, out mrIid))
        {
            return true;
        }

        mrIid = 0;
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

        if (path.Length > MaxFilePathLength)
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
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return GitLabErrors.RateLimitExhausted;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            if (response.Headers.TryGetValues("RateLimit-Remaining", out IEnumerable<string>? remaining) &&
                remaining.FirstOrDefault() == "0")
            {
                return GitLabErrors.RateLimitExhausted;
            }
        }

        int statusCode = (int)response.StatusCode;
        return GitLabErrors.UnexpectedStatusCode(statusCode);
    }

    private sealed record GitLabProjectInfoDto(string DefaultBranch);

    private sealed record GitLabIssueDto(
        int Iid,
        string Title,
        string? Description,
        GitLabAuthorDto Author,
        string WebUrl,
        IReadOnlyList<string> Labels);

    private sealed record GitLabAuthorDto(string Username);

    private sealed record GitLabIssueLinkDto(
        int Iid,
        string LinkType,
        string? State);

    private sealed record GitLabIssueStateDto(string State);

    private sealed record GitLabMergeRequestDto(string State);

    private sealed record GitLabDiscussionDto(IReadOnlyList<GitLabNoteDto> Notes);

    private sealed record GitLabNoteDto(
        string Body,
        bool Resolvable,
        bool Resolved,
        DateTimeOffset UpdatedAt,
        GitLabNotePositionDto? Position);

    private sealed record GitLabNotePositionDto(string? NewPath);

    private sealed record GitLabProjectListItemDto(
        string PathWithNamespace,
        string Visibility);

    private sealed record GitLabCompareDto(IReadOnlyList<GitLabCommitDto> Commits);

    private sealed record GitLabCommitDto(string Id);

    private sealed record GitLabMergeRequestListItemDto(string WebUrl);

    private sealed record GitLabProtectedBranchDto(
        bool AllowForcePush,
        IReadOnlyList<GitLabAccessLevelDto>? PushAccessLevels);

    private sealed record GitLabAccessLevelDto(int AccessLevel);
}

internal static class GitLabErrors
{
    public static readonly Error InvalidBaseUrl = new(
        "GitLab.InvalidBaseUrl",
        "The base URL must use the https scheme.");

    public static readonly Error RateLimitExhausted = new(
        "GitLab.RateLimitExhausted",
        "GitLab API rate limit exhausted. Wait before retrying.");

    public static readonly Error InvalidMergeRequestUrl = new(
        "GitLab.InvalidMergeRequestUrl",
        "The merge request URL does not contain a valid MR IID.");

    public static Error UnexpectedStatusCode(int statusCode) =>
        new("GitLab.UnexpectedStatusCode", $"GitLab API returned unexpected status code {statusCode}.");
}
