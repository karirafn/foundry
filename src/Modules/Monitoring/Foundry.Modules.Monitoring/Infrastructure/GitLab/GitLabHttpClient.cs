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
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed partial class GitLabHttpClient(
    HttpClient httpClient,
    ILogger<GitLabHttpClient> logger,
    DefaultBranchCache defaultBranchCache,
    IProviderRateBudget rateBudget,
    TimeProvider timeProvider)
{
    private const int MaxCommentBodyLength = 4000;
    private const string TruncatedSuffix = "[truncated]";
    private const int MaxRepositoryPages = 5;
    private const int RepositoriesPerPage = 100;
    private const int MaxIssuePages = 20;
    private const int IssuesPerPage = 100;
    private const int MaxDiscussionPages = 20;
    private const int DiscussionsPerPage = 100;
    private const int MaxFilePathLength = 4096; // PATH_MAX
    private const int GitLabMinPushAccessLevel = 30; // Developer role

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<Result<TokenValidationOutcome>> ValidateTokenAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<TokenValidationOutcome>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), "user");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Add("PRIVATE-TOKEN", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            return Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.AuthenticationFailed());
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<TokenValidationOutcome>.Fail(GitLabErrors.UnexpectedStatusCode((int)response.StatusCode));
        }

        RecordRestHeadroom(response);

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabUserDto? userDto = DeserializeUserDto(responseBody, apiBaseUrl, (int)response.StatusCode);

        if (userDto is null)
        {
            return Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.IdentityUnresolved());
        }

        string? username = string.IsNullOrEmpty(userDto.Username) ? null : userDto.Username;

        if (username is null && !string.IsNullOrEmpty(userDto.Login))
        {
            return Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.ProviderMismatch(ProviderTypes.GitHub));
        }

        if (username is null)
        {
            return Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.IdentityUnresolved());
        }

        return await ValidateScopesAsync(apiBaseUrl, token, username, cancellationToken);
    }

    private async Task<Result<TokenValidationOutcome>> ValidateScopesAsync(
        Uri apiBaseUrl,
        string token,
        string accountName,
        CancellationToken cancellationToken)
    {
        Uri selfUri = new(EnsureTrailingSlash(apiBaseUrl), "personal_access_tokens/self");

        using HttpRequestMessage selfRequest = new(HttpMethod.Get, selfUri);
        AddCommonHeaders(selfRequest, token);

        using HttpResponseMessage selfResponse = await httpClient.SendAsync(selfRequest, cancellationToken);

        if (!selfResponse.IsSuccessStatusCode)
        {
            return Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.ScopesUnverifiable(accountName));
        }

        RecordRestHeadroom(selfResponse);

        string selfBody = await selfResponse.Content.ReadAsStringAsync(cancellationToken);
        GitLabTokenSelfDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<GitLabTokenSelfDto>(selfBody, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "GitLab token validation: failed to parse response body. Provider: gitlab, Host: {Host}, Status: {StatusCode}",
                apiBaseUrl.Host,
                (int)selfResponse.StatusCode);
            return Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.ScopesUnverifiable(accountName));
        }

        HashSet<string> granted = new(dto?.Scopes ?? [], StringComparer.OrdinalIgnoreCase);
        List<string> missing = RequiredScopes.For(ProviderTypes.GitLab)
            .Where(s => !granted.Contains(s))
            .ToList();

        return Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.Authenticated(accountName, missing));
    }

    public async Task<Result<IssueListing>> GetIssuesAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IssueListing>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        List<ProviderIssue> allIssues = [];

        for (int page = 1; page <= MaxIssuePages; page++)
        {
            string relativePath = $"projects/{encodedPath}/issues?labels=foundry&state=opened&per_page={IssuesPerPage}&page={page}";
            Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

            using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            AddCommonHeaders(request, token);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result<IssueListing>.Fail(ErrorFromNonSuccess(response));
            }

            RecordRestHeadroom(response);

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            List<GitLabIssueDto>? dtos = JsonSerializer.Deserialize<List<GitLabIssueDto>>(body, JsonOptions);

            List<GitLabIssueDto> pageItems = dtos ?? [];
            foreach (GitLabIssueDto dto in pageItems)
            {
                allIssues.Add(new ProviderIssue(
                    Number: dto.Iid,
                    Title: dto.Title,
                    Author: dto.Author.Username,
                    Url: dto.WebUrl,
                    Labels: dto.Labels,
                    IssueKindLabel: LabelClassifier.ClassifyKind(dto.Labels)));
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
            return Result<IReadOnlyList<int>>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        Result<int> projectIdResult = await ResolveProjectIdAsync(apiBaseUrl, slug, token, cancellationToken);

        if (projectIdResult is not Result<int>.Success projectIdSuccess)
        {
            Error error = ((Result<int>.Failure)projectIdResult).Error;
            return Result<IReadOnlyList<int>>.Fail(error);
        }

        int resolvedProjectId = projectIdSuccess.Value;

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string relativePath = $"projects/{encodedPath}/issues/{issueNumber}/links";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            RecordRestHeadroom(response);
            return Result<IReadOnlyList<int>>.Ok([]);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<int>>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitLabIssueLinkDto>? dtos = JsonSerializer.Deserialize<List<GitLabIssueLinkDto>>(body, JsonOptions);

        IReadOnlyList<int> issueNumbers = (dtos ?? [])
            .Where(dto => string.Equals(dto.LinkType, "is_blocked_by", StringComparison.OrdinalIgnoreCase))
            .Where(dto => !string.Equals(dto.State, "closed", StringComparison.OrdinalIgnoreCase))
            .Where(dto => dto.ProjectId is null || dto.ProjectId == resolvedProjectId)
            .Select(dto => dto.Iid)
            .ToList();

        return Result<IReadOnlyList<int>>.Ok(issueNumbers);
    }

    private async Task<Result<int>> ResolveProjectIdAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        Result<GitLabProjectInfoDto> infoResult =
            await GetProjectInfoAsync(apiBaseUrl, slug, token, cancellationToken);

        if (infoResult is not Result<GitLabProjectInfoDto>.Success infoSuccess)
        {
            Error error = ((Result<GitLabProjectInfoDto>.Failure)infoResult).Error;
            return Result<int>.Fail(error);
        }

        return Result<int>.Ok(infoSuccess.Value.Id);
    }

    private async Task<Result<GitLabProjectInfoDto>> GetProjectInfoAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string relativePath = $"projects/{encodedPath}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<GitLabProjectInfoDto>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabProjectInfoDto? dto = JsonSerializer.Deserialize<GitLabProjectInfoDto>(body, JsonOptions);

        if (dto is null)
        {
            return Result<GitLabProjectInfoDto>.Fail(
                GitLabErrors.UnexpectedStatusCode((int)response.StatusCode));
        }

        return Result<GitLabProjectInfoDto>.Ok(dto);
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

        RecordRestHeadroom(response);

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

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabMergeRequestDto? dto = JsonSerializer.Deserialize<GitLabMergeRequestDto>(body, JsonOptions);

        bool isMerged = string.Equals(dto?.State, "merged", StringComparison.OrdinalIgnoreCase);
        bool isClosed = isMerged ||
            string.Equals(dto?.State, "closed", StringComparison.OrdinalIgnoreCase);

        return Result<PullRequestStatus>.Ok(new PullRequestStatus(isClosed, isMerged));
    }

    public async Task<Result<IReadOnlyList<ProviderComment>>> GetPullRequestReviewFeedbackAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<ProviderComment>>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        if (!TryParseMrIid(pullRequestUrl, out int mrIid))
        {
            return Result<IReadOnlyList<ProviderComment>>.Fail(GitLabErrors.InvalidMergeRequestUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        List<ProviderComment> providerComments = [];

        for (int page = 1; page <= MaxDiscussionPages; page++)
        {
            string relativePath =
                $"projects/{encodedPath}/merge_requests/{mrIid}/discussions?per_page={DiscussionsPerPage}&page={page}";
            Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

            using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            AddCommonHeaders(request, token);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result<IReadOnlyList<ProviderComment>>.Fail(ErrorFromNonSuccess(response));
            }

            RecordRestHeadroom(response);

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            List<GitLabDiscussionDto>? dtos = JsonSerializer.Deserialize<List<GitLabDiscussionDto>>(body, JsonOptions);
            List<GitLabDiscussionDto> pageDiscussions = dtos ?? [];

            foreach (GitLabDiscussionDto discussion in pageDiscussions)
            {
                foreach (GitLabNoteDto note in discussion.Notes)
                {
                    string? sanitizedPath = SanitizeFilePath(note.Position?.NewPath);
                    int? line = note.Position?.NewLine;
                    CommentOrigin origin = note.Resolvable ? CommentOrigin.ReviewThread : CommentOrigin.Conversation;
                    bool threadResolved = note.Resolvable && note.Resolved;

                    providerComments.Add(new ProviderComment(
                        Body: TruncateBody(note.Body),
                        AuthorLogin: note.Author?.Username ?? string.Empty,
                        AuthorIsBot: false,
                        IsSystem: note.System,
                        CreatedAt: note.CreatedAt,
                        FilePath: sanitizedPath,
                        Line: line,
                        Origin: origin,
                        ThreadResolved: threadResolved));
                }
            }

            if (pageDiscussions.Count < DiscussionsPerPage)
            {
                break;
            }
        }

        return Result<IReadOnlyList<ProviderComment>>.Ok(providerComments);
    }

    public async Task<Result<IReadOnlyList<ProviderRepository>>> ListRepositoriesAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<ProviderRepository>>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        List<ProviderRepository> repositories = [];

        for (int page = 1; page <= MaxRepositoryPages; page++)
        {
            // simple=false (default) returns the full project payload including the permissions
            // object, which carries project_access.access_level and group_access.access_level.
            // Developer (30) or above is required for push access.
            string relativePath = $"projects?membership=true&per_page={RepositoriesPerPage}&page={page}";
            Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

            using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            AddCommonHeaders(request, token);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result<IReadOnlyList<ProviderRepository>>.Fail(ErrorFromNonSuccess(response));
            }

            RecordRestHeadroom(response);

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            List<GitLabProjectListItemDto>? dtos =
                JsonSerializer.Deserialize<List<GitLabProjectListItemDto>>(body, JsonOptions);

            List<GitLabProjectListItemDto> pageItems = dtos ?? [];
            foreach (GitLabProjectListItemDto dto in pageItems)
            {
                bool isPrivate = string.Equals(dto.Visibility, "private", StringComparison.OrdinalIgnoreCase);
                int projectLevel = dto.Permissions?.ProjectAccess?.AccessLevel ?? 0;
                int groupLevel = dto.Permissions?.GroupAccess?.AccessLevel ?? 0;
                bool canPush = Math.Max(projectLevel, groupLevel) >= GitLabMinPushAccessLevel;
                repositories.Add(new ProviderRepository(dto.PathWithNamespace, isPrivate, canPush));
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
            RecordRestHeadroom(createResponse);
            return Result<bool>.Ok(false);
        }

        if (!createResponse.IsSuccessStatusCode)
        {
            return Result<bool>.Fail(ErrorFromNonSuccess(createResponse));
        }

        RecordRestHeadroom(createResponse);
        return Result<bool>.Ok(true);
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
            return Result<MergeRequestByBranch>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string encodedBranch = Uri.EscapeDataString(branchName);
        // state=all is the GitLab default but made explicit here for self-documentation: we need
        // merged MRs returned even after the source branch is deleted.
        string relativePath = $"projects/{encodedPath}/merge_requests?source_branch={encodedBranch}&state=all";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<MergeRequestByBranch>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitLabMergeRequestStateDto>? dtos =
            JsonSerializer.Deserialize<List<GitLabMergeRequestStateDto>>(body, JsonOptions);

        List<GitLabMergeRequestStateDto> items = dtos ?? [];

        if (items.Count == 0)
        {
            return Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null));
        }

        GitLabMergeRequestStateDto? merged = items.FirstOrDefault(
            dto => string.Equals(dto.State, "merged", StringComparison.OrdinalIgnoreCase));

        GitLabMergeRequestStateDto selected = merged
            ?? items.MaxBy(dto => dto.UpdatedAt)!;

        if (string.IsNullOrEmpty(selected.State))
        {
            return Result<MergeRequestByBranch>.Fail(GitLabErrors.MissingMergeRequestState);
        }

        MergeRequestPresence presence = selected.State.ToLowerInvariant() switch
        {
            "merged" => MergeRequestPresence.Merged,
            "opened" => MergeRequestPresence.Open,
            _ => MergeRequestPresence.Closed,
        };

        return Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(presence, selected.WebUrl));
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
            return Result<BranchCommitSummary>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string encodedDefault = Uri.EscapeDataString(defaultBranch);
        string encodedBranch = Uri.EscapeDataString(branchName);
        // GitLab compare defaults to merge-base (diverged) comparison — do not add straight=true.
        string relativePath = $"projects/{encodedPath}/repository/compare?from={encodedDefault}&to={encodedBranch}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<BranchCommitSummary>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabCompareDto? dto = JsonSerializer.Deserialize<GitLabCompareDto>(body, JsonOptions);

        IReadOnlyList<GitLabCommitDto> commits = dto?.Commits ?? [];
        int commitCount = commits.Count;
        string? latestSha = commits.Count > 0 ? commits[^1].Id : null;

        return Result<BranchCommitSummary>.Ok(new BranchCommitSummary(commitCount, latestSha));
    }

    public Task<Result<string>> GetDefaultBranchAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Task.FromResult(Result<string>.Fail(GitLabErrors.InvalidBaseUrl));
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
        Result<GitLabProjectInfoDto> infoResult =
            await GetProjectInfoAsync(apiBaseUrl, slug, token, cancellationToken);

        if (infoResult is not Result<GitLabProjectInfoDto>.Success infoSuccess)
        {
            Error error = ((Result<GitLabProjectInfoDto>.Failure)infoResult).Error;
            return Result<string>.Fail(error);
        }

        return Result<string>.Ok(infoSuccess.Value.DefaultBranch ?? string.Empty);
    }

    public async Task<Result<bool>> GetPushPermissionAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<bool>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string relativePath = $"projects/{encodedPath}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<bool>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabProjectWithPermissionsDto? dto =
            JsonSerializer.Deserialize<GitLabProjectWithPermissionsDto>(body, JsonOptions);

        int projectLevel = dto?.Permissions?.ProjectAccess?.AccessLevel ?? 0;
        int groupLevel = dto?.Permissions?.GroupAccess?.AccessLevel ?? 0;
        bool canPush = Math.Max(projectLevel, groupLevel) >= GitLabMinPushAccessLevel;

        return Result<bool>.Ok(canPush);
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
            RecordRestHeadroom(response);
            return Result<BranchRules>.Ok(new BranchRules(false, false, false));
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<BranchRules>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabProtectedBranchDto? dto = JsonSerializer.Deserialize<GitLabProtectedBranchDto>(body, JsonOptions);

        bool rejectDirectPushes = dto?.AllowForcePush is false &&
            (dto.PushAccessLevels ?? []).All(a => a.AccessLevel == 0);
        bool rejectForcePushes = dto?.AllowForcePush is false;
        bool rejectDeletion = !(dto?.AllowForcePush ?? true);

        return Result<BranchRules>.Ok(new BranchRules(rejectDirectPushes, rejectForcePushes, rejectDeletion));
    }

    private GitLabUserDto? DeserializeUserDto(string responseBody, Uri apiBaseUrl, int statusCode)
    {
        try
        {
            return JsonSerializer.Deserialize<GitLabUserDto>(responseBody, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "GitLab token validation: failed to parse response body. Provider: gitlab, Host: {Host}, Status: {StatusCode}",
                apiBaseUrl.Host,
                statusCode);
            return null;
        }
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

    private void RecordRestHeadroom(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("RateLimit-Remaining", out IEnumerable<string>? remainingValues))
        {
            return;
        }

        if (!int.TryParse(remainingValues.FirstOrDefault(), out int remaining))
        {
            return;
        }

        int? limit = null;
        if (response.Headers.TryGetValues("RateLimit-Limit", out IEnumerable<string>? limitValues) &&
            int.TryParse(limitValues.FirstOrDefault(), out int parsedLimit))
        {
            limit = parsedLimit;
        }

        DateTimeOffset? resetAt = null;
        if (response.Headers.TryGetValues("RateLimit-Reset", out IEnumerable<string>? resetValues) &&
            long.TryParse(resetValues.FirstOrDefault(), out long epochSeconds))
        {
            resetAt = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
        }

        rateBudget.Record(
            ProviderBudgetKey.GitLabRest,
            new RateBudgetReading(remaining, limit, resetAt, timeProvider.GetUtcNow()));
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
        Error error = GitLabErrors.UnexpectedStatusCode(statusCode);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return error with { Kind = ErrorKind.NotFound };
        }

        return error;
    }

    private sealed record GitLabUserDto(
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("login")] string Login);

    private sealed record GitLabTokenSelfDto([property: JsonPropertyName("scopes")] IReadOnlyList<string> Scopes);

    private sealed record GitLabProjectInfoDto(int Id, string DefaultBranch);

    private sealed record GitLabIssueDto(
        int Iid,
        string Title,
        GitLabAuthorDto Author,
        string WebUrl,
        IReadOnlyList<string> Labels);

    private sealed record GitLabAuthorDto(string Username);

    private sealed record GitLabIssueLinkDto(
        int Iid,
        string LinkType,
        string? State,
        int? ProjectId);

    private sealed record GitLabIssueStateDto(string State);

    private sealed record GitLabMergeRequestDto(string State);

    private sealed record GitLabDiscussionDto(IReadOnlyList<GitLabNoteDto> Notes);

    private sealed record GitLabNoteDto(
        string Body,
        bool Resolvable,
        bool Resolved,
        bool System,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        GitLabAuthorDto? Author,
        GitLabNotePositionDto? Position);

    private sealed record GitLabNotePositionDto(string? NewPath, int? NewLine);

    private sealed record GitLabProjectPermissionsDto(
        GitLabAccessLevelDto? ProjectAccess,
        GitLabAccessLevelDto? GroupAccess);

    private sealed record GitLabProjectListItemDto(
        string PathWithNamespace,
        string Visibility,
        GitLabProjectPermissionsDto? Permissions);

    private sealed record GitLabCompareDto(IReadOnlyList<GitLabCommitDto> Commits);

    private sealed record GitLabCommitDto(string Id);

    private sealed record GitLabMergeRequestStateDto(string State, string WebUrl, DateTimeOffset UpdatedAt);

    private sealed record GitLabProjectWithPermissionsDto(GitLabProjectPermissionsDto? Permissions);

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

    public static readonly Error MissingMergeRequestState = new(
        "GitLab.MissingMergeRequestState",
        "A merge request in the response has a missing or empty state field.");

    public static Error UnexpectedStatusCode(int statusCode) =>
        new("GitLab.UnexpectedStatusCode", $"GitLab API returned unexpected status code {statusCode}.");
}
