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
    TimeProvider timeProvider) : IGitLabTokenGrantReader
{
    private const int MaxIssuePages = 20;
    private const int IssuesPerPage = 100;
    private const int ProjectsPerPage = 100;
    private const int MaxDiscussionPages = 20;
    private const int DiscussionsPerPage = 100;
    private const int MaxFilePathLength = 4096; // PATH_MAX
    // Which repositories the token can push to (namespace claims), not what Foundry needs: stays Developer.
    private const int GitLabWritableRepositoryAccessLevel = 30; // Developer role

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
        Result<GitLabTokenSelf> self = await ReadTokenSelfAsync(apiBaseUrl, token, cancellationToken);

        return self switch
        {
            Result<GitLabTokenSelf>.Success { Value: GitLabTokenSelf.FineGrained fineGrained } =>
                Result<TokenValidationOutcome>.Ok(
                    TokenValidationOutcome.GrantRead(accountName, fineGrained.Grant, fineGrained.Expiry)),
            Result<GitLabTokenSelf>.Success { Value: GitLabTokenSelf.OfWrongKind wrongKind } =>
                Result<TokenValidationOutcome>.Ok(
                    TokenValidationOutcome.TokenOfWrongKind(accountName, wrongKind.Kind, wrongKind.Expiry)),
            Result<GitLabTokenSelf>.Success { Value: GitLabTokenSelf.CannotReadItself } =>
                Result<TokenValidationOutcome>.Ok(TokenValidationOutcome.Authenticated(
                    accountName,
                    new TokenExpiry.Unavailable(),
                    TokenPermissions.LabelsOf(ProviderTypes.GitLab, [TokenPermissions.GitLabReadOwnTokenKey]))),
            Result<GitLabTokenSelf>.Failure failure => Result<TokenValidationOutcome>.Fail(failure.Error),
            _ => throw new InvalidOperationException("Unexpected result."),
        };
    }

    /// <summary>
    /// Reads <c>GET /personal_access_tokens/self</c>. A fine-grained token (GitLab 19.5 and later) reports its
    /// grant in <c>granular_scopes</c>; one lacking <c>read_personal_access_token</c> is refused with
    /// <c>insufficient_granular_scope</c>. A body without <c>granular_scopes</c> is a legacy token, or a
    /// fine-grained one on a GitLab too old to report its grant, and any other refusal is a group or
    /// project access token, which cannot read the endpoint. A body that cannot be read fails.
    /// </summary>
    public async Task<Result<GitLabTokenSelf>> ReadTokenSelfAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<GitLabTokenSelf>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        Uri selfUri = new(EnsureTrailingSlash(apiBaseUrl), "personal_access_tokens/self");

        using HttpRequestMessage selfRequest = new(HttpMethod.Get, selfUri);
        AddCommonHeaders(selfRequest, token);

        using HttpResponseMessage selfResponse = await httpClient.SendAsync(selfRequest, cancellationToken);
        string selfBody = await selfResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!selfResponse.IsSuccessStatusCode)
        {
            if (selfResponse.StatusCode is HttpStatusCode.Forbidden && IsInsufficientGranularScope(selfBody))
            {
                RecordRestHeadroom(selfResponse);
                return Result<GitLabTokenSelf>.Ok(new GitLabTokenSelf.CannotReadItself());
            }

            // A throttled or failing GitLab says nothing about the token, so it is a failure, not a token kind.
            if (selfResponse.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError
                || (selfResponse.StatusCode is HttpStatusCode.Forbidden && IsRateLimited(selfResponse)))
            {
                return Result<GitLabTokenSelf>.Fail(ErrorFromNonSuccess(selfResponse));
            }

            return Result<GitLabTokenSelf>.Ok(
                new GitLabTokenSelf.OfWrongKind(WrongTokenKind.GitLabResourceToken, new TokenExpiry.Unavailable()));
        }

        RecordRestHeadroom(selfResponse);

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
            return Result<GitLabTokenSelf>.Fail(GitLabErrors.UnexpectedStatusCode((int)selfResponse.StatusCode));
        }

        if (dto is null)
        {
            return Result<GitLabTokenSelf>.Fail(GitLabErrors.UnexpectedStatusCode((int)selfResponse.StatusCode));
        }

        TokenExpiry tokenExpiry = ReadTokenExpiry(dto.ExpiresAt, apiBaseUrl);

        if (dto.GranularScopes is null)
        {
            bool isFineGrained = (dto.Scopes ?? []).Contains("granular", StringComparer.OrdinalIgnoreCase);
            return Result<GitLabTokenSelf>.Ok(new GitLabTokenSelf.OfWrongKind(
                isFineGrained ? WrongTokenKind.GitLabVersion : WrongTokenKind.GitLabLegacy,
                tokenExpiry));
        }

        GitLabTokenGrant grant = GitLabTokenGrant.Create(dto.GranularScopes
            .Select(entry => new GitLabTokenGrantEntry(
                entry.Access ?? string.Empty,
                entry.Permissions ?? [],
                entry.ProjectId,
                entry.GroupId)));

        return Result<GitLabTokenSelf>.Ok(new GitLabTokenSelf.FineGrained(grant, tokenExpiry));
    }

    private static bool IsInsufficientGranularScope(string body)
    {
        try
        {
            GitLabErrorBodyDto? error = JsonSerializer.Deserialize<GitLabErrorBodyDto>(body, JsonOptions);
            return string.Equals(error?.Error, "insufficient_granular_scope", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Maps <c>expires_at</c> from <c>/personal_access_tokens/self</c>. GitLab sends a date with no time,
    /// and the token expires at midnight UTC on that date, so the date maps to its start: a day early,
    /// never late. A null value means the token never expires.
    /// </summary>
    private TokenExpiry ReadTokenExpiry(JsonElement expiresAt, Uri apiBaseUrl)
    {
        if (expiresAt.ValueKind is JsonValueKind.Null)
        {
            return new TokenExpiry.Never();
        }

        if (expiresAt.ValueKind is JsonValueKind.String
            && DateOnly.TryParseExact(
                expiresAt.GetString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly date))
        {
            return new TokenExpiry.On(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        }

        logger.LogWarning(
            "GitLab token validation: expires_at is not a yyyy-MM-dd date; expiry recorded as unavailable. Provider: gitlab, Host: {Host}",
            apiBaseUrl.Host);
        return new TokenExpiry.Unavailable();
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

    public async Task<Result<int>> ResolveProjectIdAsync(
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
        Result<GitLabIssueStateDto> result = await ReadIssueAsync(apiBaseUrl, slug, issueNumber, token, cancellationToken);

        return result.Match(
            dto => Result<bool>.Ok(IsClosed(dto)),
            Result<bool>.Fail);
    }

    /// <summary>
    /// Reads the issue's state and description with the same <c>GET projects/:id/issues/:iid</c> request
    /// <see cref="IsIssueClosedAsync"/> sends; a missing description is the empty string.
    /// </summary>
    public async Task<Result<IssueStatus>> GetIssueStatusAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueNumber,
        string token,
        CancellationToken cancellationToken)
    {
        Result<GitLabIssueStateDto> result = await ReadIssueAsync(apiBaseUrl, slug, issueNumber, token, cancellationToken);

        return result.Match(
            dto => Result<IssueStatus>.Ok(new IssueStatus(IsClosed(dto), dto.Description ?? string.Empty)),
            Result<IssueStatus>.Fail);
    }

    private static bool IsClosed(GitLabIssueStateDto dto) =>
        string.Equals(dto.State, "closed", StringComparison.OrdinalIgnoreCase);

    private async Task<Result<GitLabIssueStateDto>> ReadIssueAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueNumber,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<GitLabIssueStateDto>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string relativePath = $"projects/{encodedPath}/issues/{issueNumber}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<GitLabIssueStateDto>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabIssueStateDto dto = JsonSerializer.Deserialize<GitLabIssueStateDto>(body, JsonOptions)
            ?? new GitLabIssueStateDto(State: null, Description: null);

        return Result<GitLabIssueStateDto>.Ok(dto);
    }

    public async Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        string token,
        CancellationToken cancellationToken)
    {
        Result<MergeRequestSnapshot> snapshot = await GetMergeRequestSnapshotAsync(
            apiBaseUrl,
            slug,
            pullRequestUrl,
            token,
            cancellationToken);

        return snapshot switch
        {
            Result<MergeRequestSnapshot>.Success success => Result<PullRequestStatus>.Ok(success.Value.Status),
            Result<MergeRequestSnapshot>.Failure failure => Result<PullRequestStatus>.Fail(failure.Error),
            _ => throw new InvalidOperationException("Unexpected result."),
        };
    }

    /// <summary>
    /// Reads one merge request — <c>GET projects/:id/merge_requests/:iid</c> — and returns both its
    /// open/closed/merged status and the id and status of its <c>head_pipeline</c>, so a checks pass
    /// needs a single fetch per merge request. The pipeline's failed jobs are read later, by
    /// <see cref="GetPipelineChecksAsync"/>, only when the checks verdict is consumed. Any non-success
    /// response is a failure.
    /// </summary>
    public async Task<Result<MergeRequestSnapshot>> GetMergeRequestSnapshotAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<MergeRequestSnapshot>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        if (!TryParseMrIid(pullRequestUrl, out int mrIid))
        {
            return Result<MergeRequestSnapshot>.Fail(GitLabErrors.InvalidMergeRequestUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string relativePath = $"projects/{encodedPath}/merge_requests/{mrIid}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<MergeRequestSnapshot>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabMergeRequestWithPipelineDto? dto =
            JsonSerializer.Deserialize<GitLabMergeRequestWithPipelineDto>(body, JsonOptions);

        bool isMerged = string.Equals(dto?.State, "merged", StringComparison.OrdinalIgnoreCase);
        bool isClosed = isMerged ||
            string.Equals(dto?.State, "closed", StringComparison.OrdinalIgnoreCase);

        return Result<MergeRequestSnapshot>.Ok(new MergeRequestSnapshot(
            new PullRequestStatus(isClosed, isMerged, dto?.Sha, dto?.Draft ?? false),
            dto?.HeadPipeline?.Id,
            dto?.HeadPipeline?.Status));
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
                        Id: ProviderCommentId.FromProvider(note.Id),
                        Body: note.Body,
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

    /// <summary>
    /// Lists the projects the token's grant reaches: every project its user is a member of when it reaches all
    /// memberships, otherwise exactly the projects it selects, each read with <c>GET /projects/:id</c>. A token
    /// that reports no grant selects no project.
    /// </summary>
    public async Task<Result<IReadOnlyList<ProviderRepository>>> ListRepositoriesAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<ProviderRepository>>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        Result<GitLabTokenSelf> self = await ReadTokenSelfAsync(apiBaseUrl, token, cancellationToken);
        if (self is Result<GitLabTokenSelf>.Failure selfFailure)
        {
            return Result<IReadOnlyList<ProviderRepository>>.Fail(selfFailure.Error);
        }

        if (self is not Result<GitLabTokenSelf>.Success { Value: GitLabTokenSelf.FineGrained fineGrained })
        {
            return Result<IReadOnlyList<ProviderRepository>>.Ok([]);
        }

        if (fineGrained.Grant.ReachesAllMemberships)
        {
            return await ListMemberProjectsAsync(apiBaseUrl, token, cancellationToken);
        }

        List<ProviderRepository> repositories = [];

        foreach (long projectId in fineGrained.Grant.ProjectIds)
        {
            Uri requestUri = new(
                EnsureTrailingSlash(apiBaseUrl),
                $"projects/{projectId.ToString(CultureInfo.InvariantCulture)}");

            using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            AddCommonHeaders(request, token);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result<IReadOnlyList<ProviderRepository>>.Fail(ErrorFromNonSuccess(response));
            }

            RecordRestHeadroom(response);

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            GitLabProjectListItemDto? dto = JsonSerializer.Deserialize<GitLabProjectListItemDto>(body, JsonOptions);

            if (dto is null)
            {
                return Result<IReadOnlyList<ProviderRepository>>.Fail(
                    GitLabErrors.UnexpectedStatusCode((int)response.StatusCode));
            }

            repositories.Add(ToProviderRepository(dto));
        }

        return Result<IReadOnlyList<ProviderRepository>>.Ok(repositories);
    }

    private async Task<Result<IReadOnlyList<ProviderRepository>>> ListMemberProjectsAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        List<ProviderRepository> repositories = [];

        for (int page = 1; ; page++)
        {
            Uri requestUri = new(
                EnsureTrailingSlash(apiBaseUrl),
                $"projects?membership=true&per_page={ProjectsPerPage}&page={page.ToString(CultureInfo.InvariantCulture)}");

            using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            AddCommonHeaders(request, token);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result<IReadOnlyList<ProviderRepository>>.Fail(ErrorFromNonSuccess(response));
            }

            RecordRestHeadroom(response);

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            List<GitLabProjectListItemDto> pageItems =
                JsonSerializer.Deserialize<List<GitLabProjectListItemDto>>(body, JsonOptions) ?? [];

            repositories.AddRange(pageItems.Select(ToProviderRepository));

            if (pageItems.Count < ProjectsPerPage)
            {
                return Result<IReadOnlyList<ProviderRepository>>.Ok(repositories);
            }
        }
    }

    private static ProviderRepository ToProviderRepository(GitLabProjectListItemDto dto)
    {
        // The permissions object carries project_access and group_access levels; Developer (30) or above can push.
        bool isPrivate = string.Equals(dto.Visibility, "private", StringComparison.OrdinalIgnoreCase);
        int projectLevel = dto.Permissions?.ProjectAccess?.AccessLevel ?? 0;
        int groupLevel = dto.Permissions?.GroupAccess?.AccessLevel ?? 0;
        bool canPush = Math.Max(projectLevel, groupLevel) >= GitLabWritableRepositoryAccessLevel;
        return new ProviderRepository(dto.PathWithNamespace, isPrivate, canPush);
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

    /// <summary>
    /// Reads where <paramref name="branchName"/> points with <c>GET projects/:id/repository/branches/:branch</c>,
    /// returning <c>commit.id</c>. GitLab answers 404 for a missing branch, which is <see cref="BranchHead.Absent"/>;
    /// any other non-success response fails.
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
            return Result<BranchHead>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string encodedBranch = Uri.EscapeDataString(branchName);
        string relativePath = $"projects/{encodedPath}/repository/branches/{encodedBranch}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
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
        string? sha = JsonSerializer.Deserialize<GitLabBranchDto>(body, JsonOptions)?.Commit?.Id;

        return !string.IsNullOrWhiteSpace(sha)
            ? Result<BranchHead>.Ok(new BranchHead.Present(sha))
            : Result<BranchHead>.Fail(GitLabErrors.ProviderError("GitLab returned a branch without a commit id."));
    }

    /// <summary>
    /// Deletes <paramref name="branchName"/> from the project. Returns <c>true</c> when the branch
    /// was deleted and <c>false</c> when it was already absent (404), so a repeated discard is
    /// idempotent.
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
            return Result<bool>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string encodedBranch = Uri.EscapeDataString(branchName);
        string relativePath = $"projects/{encodedPath}/repository/branches/{encodedBranch}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Delete, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
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

        if (items.Any(dto => string.IsNullOrEmpty(dto.State)))
        {
            return Result<MergeRequestByBranch>.Fail(GitLabErrors.MissingMergeRequestState);
        }

        List<MergeRequestCandidate> candidates = items.Select(ToCandidate).ToList();

        return Result<MergeRequestByBranch>.Ok(MergeRequestSelection.Select(candidates, publishedHead));
    }

    private static MergeRequestCandidate ToCandidate(GitLabMergeRequestStateDto dto)
    {
        MergeRequestPresence presence = dto.State.ToLowerInvariant() switch
        {
            "merged" => MergeRequestPresence.Merged,
            "opened" => MergeRequestPresence.Open,
            _ => MergeRequestPresence.Closed,
        };
        CommitSha? head = CommitSha.TryFrom(dto.Sha, out CommitSha sha) ? sha : null;

        return new MergeRequestCandidate(presence, dto.WebUrl, dto.UpdatedAt, head);
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
        bool canPush = Math.Max(projectLevel, groupLevel) >= TokenPermissions.GitLabMinimumAccessLevel;

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

    public async Task<Result<Checks>> GetChecksAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<Checks>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        if (!TryParseMrIid(pullRequestUrl, out int mrIid))
        {
            return Result<Checks>.Fail(GitLabErrors.InvalidMergeRequestUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string relativePath = $"projects/{encodedPath}/merge_requests/{mrIid}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        // 401/403 without rate-limit exhaustion signals an auth failure — return Unreadable
        // rather than propagating a Result failure so callers can proceed with degraded behaviour.
        if (IsAuthFailure(response))
        {
            return Result<Checks>.Ok(new Checks.Unreadable());
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<Checks>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabMergeRequestWithPipelineDto? dto =
            JsonSerializer.Deserialize<GitLabMergeRequestWithPipelineDto>(body, JsonOptions);

        return await GetPipelineChecksAsync(
            apiBaseUrl,
            slug,
            dto?.HeadPipeline?.Id,
            dto?.HeadPipeline?.Status,
            token,
            cancellationToken);
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

    private static bool IsRateLimited(HttpResponseMessage response) =>
        response.Headers.TryGetValues("RateLimit-Remaining", out IEnumerable<string>? remaining) &&
        remaining.FirstOrDefault() == "0";

    private static Error ErrorFromNonSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return GitLabErrors.RateLimitExhausted;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden && IsRateLimited(response))
        {
            return GitLabErrors.RateLimitExhausted;
        }

        int statusCode = (int)response.StatusCode;
        Error error = GitLabErrors.UnexpectedStatusCode(statusCode);

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

    private sealed record GitLabUserDto(
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("login")] string Login);

    // ExpiresAt is a JsonElement so an explicit null (never expires) is told apart from a missing field.
    private sealed record GitLabTokenSelfDto(
        [property: JsonPropertyName("scopes")] IReadOnlyList<string>? Scopes,
        [property: JsonPropertyName("expires_at")] JsonElement ExpiresAt,
        [property: JsonPropertyName("granular_scopes")] IReadOnlyList<GitLabGranularScopeDto>? GranularScopes);

    private sealed record GitLabGranularScopeDto(
        [property: JsonPropertyName("access")] string? Access,
        [property: JsonPropertyName("permissions")] IReadOnlyList<string>? Permissions,
        [property: JsonPropertyName("project_id")] long? ProjectId,
        [property: JsonPropertyName("group_id")] long? GroupId);

    private sealed record GitLabErrorBodyDto([property: JsonPropertyName("error")] string? Error);

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

    private sealed record GitLabIssueStateDto(string? State, string? Description);

    private sealed record GitLabBranchDto(GitLabBranchCommitDto? Commit);

    private sealed record GitLabBranchCommitDto(string? Id);

    private sealed record GitLabMergeRequestWithPipelineDto(
        string State,
        string? Sha,
        bool Draft,
        GitLabPipelineDto? HeadPipeline);

    private sealed record GitLabPipelineDto(long? Id, string? Status);

    private sealed record GitLabDiscussionDto(IReadOnlyList<GitLabNoteDto> Notes);

    private sealed record GitLabNoteDto(
        long? Id,
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

    private sealed record GitLabMergeRequestStateDto(string State, string WebUrl, DateTimeOffset UpdatedAt, string? Sha);

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

    private const string UnexpectedStatusCodeCode = "GitLab.UnexpectedStatusCode";
    private const string UnexpectedStatusCodePrefix = "GitLab API returned unexpected status code ";

    public static Error UnexpectedStatusCode(int statusCode) =>
        new(UnexpectedStatusCodeCode, $"{UnexpectedStatusCodePrefix}{statusCode}.");

    public static bool TryGetStatusCode(Error error, out int statusCode)
    {
        statusCode = 0;

        return string.Equals(error.Code, UnexpectedStatusCodeCode, StringComparison.Ordinal)
            && error.Message.StartsWith(UnexpectedStatusCodePrefix, StringComparison.Ordinal)
            && int.TryParse(error.Message[UnexpectedStatusCodePrefix.Length..].TrimEnd('.'), out statusCode);
    }

    public static Error ProviderError(string message) =>
        new("GitLab.ProviderError", message);

    public static Error RootDirectoryTooLarge(int maxEntries) =>
        new("GitLab.RootDirectoryTooLarge", $"The repository's root directory has more than {maxEntries} entries.");
}
