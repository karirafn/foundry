using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Publishing;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed partial class GitLabHttpClient
{
    /// <summary>
    /// Opens a merge request from <paramref name="sourceBranch"/> into <paramref name="targetBranch"/>
    /// (<c>POST /projects/:id/merge_requests</c>), keeping the source branch on merge so the issue branch stays
    /// Foundry's to manage. GitLab's 409 (an open merge request already exists for the source branch) answers
    /// <see cref="PullRequestCreation.AlreadyOpen"/>; every other failure is a <see cref="RunPublishingErrors"/>
    /// transient or permanent error.
    /// </summary>
    public async Task<Result<PullRequestCreation>> CreateMergeRequestAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string targetBranch,
        string sourceBranch,
        string title,
        string description,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<PullRequestCreation>.Fail(RunPublishingErrors.Permanent(GitLabErrors.InvalidBaseUrl.Message));
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string action = $"Opening a merge request from '{sourceBranch}' on {slug}";

        using HttpRequestMessage request = new(
            HttpMethod.Post,
            new Uri(EnsureTrailingSlash(apiBaseUrl), $"projects/{encodedPath}/merge_requests"));
        AddCommonHeaders(request, token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(
                new GitLabMergeRequestWriteDto(sourceBranch, targetBranch, title, description, RemoveSourceBranch: false),
                JsonOptions),
            Encoding.UTF8,
            "application/json");

        Result<HttpResponseMessage> sent = await SendPublishingRequestAsync(request, action, cancellationToken);
        if (sent is not Result<HttpResponseMessage>.Success { Value: { } response })
        {
            return Result<PullRequestCreation>.Fail(((Result<HttpResponseMessage>.Failure)sent).Error);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Conflict)
            {
                RecordRestHeadroom(response);
                return Result<PullRequestCreation>.Ok(new PullRequestCreation.AlreadyOpen());
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result<PullRequestCreation>.Fail(ClassifyPublishingFailure(response, action));
            }

            RecordRestHeadroom(response);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            GitLabCreatedMergeRequestDto? created =
                JsonSerializer.Deserialize<GitLabCreatedMergeRequestDto>(responseBody, JsonOptions);

            // A retry finds the merge request this call created through the already-exists path.
            return string.IsNullOrWhiteSpace(created?.WebUrl)
                ? Result<PullRequestCreation>.Fail(RunPublishingErrors.Transient(
                    $"{action}: GitLab accepted the request but returned no merge request URL."))
                : Result<PullRequestCreation>.Ok(new PullRequestCreation.Opened(created.WebUrl));
        }
    }

    /// <summary>
    /// Lists the web URLs of the open merge requests from <paramref name="sourceBranch"/>
    /// (<c>GET /projects/:id/merge_requests?source_branch=…&amp;state=opened</c>).
    /// </summary>
    public async Task<Result<IReadOnlyList<string>>> ListOpenMergeRequestUrlsAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string sourceBranch,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<string>>.Fail(RunPublishingErrors.Permanent(GitLabErrors.InvalidBaseUrl.Message));
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string encodedBranch = Uri.EscapeDataString(sourceBranch);
        string action = $"Looking up the open merge request from '{sourceBranch}' on {slug}";

        using HttpRequestMessage request = new(
            HttpMethod.Get,
            new Uri(
                EnsureTrailingSlash(apiBaseUrl),
                $"projects/{encodedPath}/merge_requests?source_branch={encodedBranch}&state=opened"));
        AddCommonHeaders(request, token);

        Result<HttpResponseMessage> sent = await SendPublishingRequestAsync(request, action, cancellationToken);
        if (sent is not Result<HttpResponseMessage>.Success { Value: { } response })
        {
            return Result<IReadOnlyList<string>>.Fail(((Result<HttpResponseMessage>.Failure)sent).Error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Result<IReadOnlyList<string>>.Fail(ClassifyPublishingFailure(response, action));
            }

            RecordRestHeadroom(response);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            List<GitLabCreatedMergeRequestDto> mergeRequests =
                JsonSerializer.Deserialize<List<GitLabCreatedMergeRequestDto>>(responseBody, JsonOptions) ?? [];

            return Result<IReadOnlyList<string>>.Ok(
                [.. mergeRequests.Select(mr => mr.WebUrl).OfType<string>().Where(url => url.Length > 0)]);
        }
    }

    /// <summary>
    /// Reads the token owner (<c>GET /user</c>) as a commit identity: the profile name, else the username, and the
    /// commit email when set, else the public email, else GitLab's private commit address
    /// <c>&lt;id&gt;-&lt;username&gt;@users.noreply.&lt;host&gt;</c>. The commit email is the address the
    /// account owner chose for commits, and the private address is the one GitLab itself uses to attribute
    /// commits to the account without exposing an email.
    /// </summary>
    public async Task<Result<CommitIdentity>> GetAuthenticatedIdentityAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<CommitIdentity>.Fail(RunPublishingErrors.Permanent(GitLabErrors.InvalidBaseUrl.Message));
        }

        const string action = "Reading the GitLab account identity";

        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(EnsureTrailingSlash(apiBaseUrl), "user"));
        AddCommonHeaders(request, token);

        Result<HttpResponseMessage> sent = await SendPublishingRequestAsync(request, action, cancellationToken);
        if (sent is not Result<HttpResponseMessage>.Success { Value: { } response })
        {
            return Result<CommitIdentity>.Fail(((Result<HttpResponseMessage>.Failure)sent).Error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Result<CommitIdentity>.Fail(ClassifyPublishingFailure(response, action));
            }

            RecordRestHeadroom(response);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            GitLabIdentityDto? user = JsonSerializer.Deserialize<GitLabIdentityDto>(responseBody, JsonOptions);

            if (user is not { Id: > 0 } || string.IsNullOrWhiteSpace(user.Username))
            {
                return Result<CommitIdentity>.Fail(RunPublishingErrors.Permanent(
                    $"{action}: GitLab returned no account ID or username."));
            }

            string name = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name.Trim();
            string email = FirstPresent(user.CommitEmail, user.PublicEmail)
                ?? $"{user.Id}-{user.Username}@users.noreply.{apiBaseUrl.Host}";
            return Result<CommitIdentity>.Ok(new CommitIdentity(name, email));
        }
    }

    /// <summary>
    /// Whether <paramref name="ancestorRef"/> is an ancestor of, or equal to, the head of
    /// <paramref name="branchName"/>: resolves the ancestor's SHA
    /// (<c>GET /projects/:id/repository/commits/:sha</c>), then compares it with the merge base of both refs
    /// (<c>GET /projects/:id/repository/merge_base</c>). A 404 on either read, or GitLab's 400 for a ref it
    /// cannot find, means a ref is missing.
    /// </summary>
    public async Task<Result<bool>> IsAncestorAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string ancestorRef,
        string branchName,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<bool>.Fail(RunPublishingErrors.Permanent(GitLabErrors.InvalidBaseUrl.Message));
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string encodedAncestor = Uri.EscapeDataString(ancestorRef);
        string encodedBranch = Uri.EscapeDataString(branchName);

        Result<string> ancestorSha = await ReadCommitIdAsync(
            apiBaseUrl,
            $"projects/{encodedPath}/repository/commits/{encodedAncestor}",
            $"Resolving '{ancestorRef}' on {slug}",
            notFoundStatuses: [HttpStatusCode.NotFound],
            token,
            cancellationToken);

        if (ancestorSha is not Result<string>.Success ancestor)
        {
            return Result<bool>.Fail(((Result<string>.Failure)ancestorSha).Error);
        }

        Result<string> mergeBaseSha = await ReadCommitIdAsync(
            apiBaseUrl,
            $"projects/{encodedPath}/repository/merge_base?refs%5B%5D={encodedAncestor}&refs%5B%5D={encodedBranch}",
            $"Reading the merge base of '{ancestorRef}' and '{branchName}' on {slug}",
            notFoundStatuses: [HttpStatusCode.NotFound, HttpStatusCode.BadRequest],
            token,
            cancellationToken);

        return mergeBaseSha switch
        {
            Result<string>.Success mergeBase => Result<bool>.Ok(
                string.Equals(mergeBase.Value, ancestor.Value, StringComparison.OrdinalIgnoreCase)),
            Result<string>.Failure failure => Result<bool>.Fail(failure.Error),
            _ => throw new UnreachableException(),
        };
    }

    private async Task<Result<string>> ReadCommitIdAsync(
        Uri apiBaseUrl,
        string relativePath,
        string action,
        IReadOnlyCollection<HttpStatusCode> notFoundStatuses,
        string token,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(EnsureTrailingSlash(apiBaseUrl), relativePath));
        AddCommonHeaders(request, token);

        Result<HttpResponseMessage> sent = await SendPublishingRequestAsync(request, action, cancellationToken);
        if (sent is not Result<HttpResponseMessage>.Success { Value: { } response })
        {
            return Result<string>.Fail(((Result<HttpResponseMessage>.Failure)sent).Error);
        }

        using (response)
        {
            if (notFoundStatuses.Contains(response.StatusCode))
            {
                RecordRestHeadroom(response);
                return Result<string>.Fail(RunPublishingErrors.RefNotFound($"{action}: a ref does not exist."));
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result<string>.Fail(ClassifyPublishingFailure(response, action));
            }

            RecordRestHeadroom(response);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            GitLabCommitIdDto? commit = JsonSerializer.Deserialize<GitLabCommitIdDto>(responseBody, JsonOptions);

            return string.IsNullOrWhiteSpace(commit?.Id)
                ? Result<string>.Fail(RunPublishingErrors.Permanent($"{action}: GitLab returned no commit ID."))
                : Result<string>.Ok(commit.Id);
        }
    }

    private static string? FirstPresent(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim();

    private async Task<Result<HttpResponseMessage>> SendPublishingRequestAsync(
        HttpRequestMessage request,
        string action,
        CancellationToken cancellationToken)
    {
        try
        {
            return Result<HttpResponseMessage>.Ok(await httpClient.SendAsync(request, cancellationToken));
        }
        catch (Exception ex) when (CommentPostErrors.IsTransportFailure(ex, cancellationToken))
        {
            return Result<HttpResponseMessage>.Fail(RunPublishingErrors.Transient(
                $"{action} did not complete: {ex.GetType().Name}."));
        }
    }

    private static Error ClassifyPublishingFailure(HttpResponseMessage response, string action)
    {
        string message = $"{action} returned {(int)response.StatusCode}.";
        return IsRetryableStatus(response) ? RunPublishingErrors.Transient(message) : RunPublishingErrors.Permanent(message);
    }

    private static bool IsRetryableStatus(HttpResponseMessage response) =>
        (int)response.StatusCode >= 500
        || response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
        || (response.StatusCode is HttpStatusCode.Forbidden && IsRateLimited(response));

    private sealed record GitLabMergeRequestWriteDto(
        string SourceBranch,
        string TargetBranch,
        string Title,
        string Description,
        bool RemoveSourceBranch);

    private sealed record GitLabCreatedMergeRequestDto(string? WebUrl);

    private sealed record GitLabIdentityDto(
        long? Id,
        string? Username,
        string? Name,
        string? CommitEmail,
        string? PublicEmail);

    private sealed record GitLabCommitIdDto(string? Id);
}
