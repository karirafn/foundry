using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed partial class GitLabHttpClient
{
    /// <summary>
    /// Posts each inline comment as its own diff discussion (<c>POST /projects/:id/merge_requests/:iid/discussions</c>)
    /// positioned on the latest merge request version (<c>GET .../versions</c>). Each response carries the new note's
    /// ID, so each comment is registered on its own, and a comment GitLab rejects fails alone.
    /// </summary>
    /// <remarks>
    /// Draft notes would post in one notification, but publishing them returns no note IDs to register.
    /// </remarks>
    public async Task<Result<InlineReviewPosting>> PostMergeRequestDiscussionsAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string mergeRequestUrl,
        string commitSha,
        IReadOnlyList<InlineComment> comments,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<InlineReviewPosting>.Fail(CommentPostErrors.Permanent(GitLabErrors.InvalidBaseUrl.Message));
        }

        if (!TryParseMrIid(mergeRequestUrl, out int mrIid) || mrIid <= 0)
        {
            return Result<InlineReviewPosting>.Fail(CommentPostErrors.Permanent(
                $"'{mergeRequestUrl}' does not name a valid merge request IID."));
        }

        string mergeRequestPath = $"projects/{Uri.EscapeDataString(slug.FullPath)}/merge_requests/{mrIid}";
        Result<GitLabMergeRequestVersionDto> version =
            await GetLatestVersionAsync(apiBaseUrl, slug, mergeRequestPath, mrIid, token, cancellationToken);

        if (version is not Result<GitLabMergeRequestVersionDto>.Success { Value: var latest })
        {
            return Result<InlineReviewPosting>.Fail(((Result<GitLabMergeRequestVersionDto>.Failure)version).Error);
        }

        // Line numbers come from the reviewed commit's diff, so they only hold on a version whose head is that commit.
        if (!string.Equals(latest.HeadCommitSha, commitSha, StringComparison.OrdinalIgnoreCase))
        {
            return Result<InlineReviewPosting>.Fail(CommentPostErrors.Permanent(
                $"Merge request {slug}!{mrIid} has moved past the reviewed commit {commitSha}."));
        }

        List<PostedInlineComment> posted = [];
        List<FailedInlineComment> failed = [];

        foreach (InlineComment comment in comments)
        {
            Result<ProviderCommentId> note =
                await PostDiscussionAsync(apiBaseUrl, slug, mergeRequestPath, mrIid, latest, comment, token, cancellationToken);

            if (note is Result<ProviderCommentId>.Success { Value: var noteId })
            {
                posted.Add(new PostedInlineComment(comment, PostedCommentKey.ReviewNote(noteId)));
            }
            else
            {
                failed.Add(new FailedInlineComment(comment, ((Result<ProviderCommentId>.Failure)note).Error));
            }
        }

        return Result<InlineReviewPosting>.Ok(new InlineReviewPosting(posted, failed));
    }

    private async Task<Result<GitLabMergeRequestVersionDto>> GetLatestVersionAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string mergeRequestPath,
        int mrIid,
        string token,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            new Uri(EnsureTrailingSlash(apiBaseUrl), $"{mergeRequestPath}/versions?per_page=1"));
        AddCommonHeaders(request, token);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (CommentPostErrors.IsTransportFailure(ex, cancellationToken))
        {
            return Result<GitLabMergeRequestVersionDto>.Fail(CommentPostErrors.Transient(
                $"Reading the versions of {slug}!{mrIid} did not complete: {ex.GetType().Name}."));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Result<GitLabMergeRequestVersionDto>.Fail(
                    ClassifyReviewFailure(response, $"Reading the versions of {slug}!{mrIid}"));
            }

            RecordRestHeadroom(response);

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            List<GitLabMergeRequestVersionDto>? versions =
                JsonSerializer.Deserialize<List<GitLabMergeRequestVersionDto>>(body, JsonOptions);

            // GitLab lists versions newest first.
            return versions is [{ HeadCommitSha: { }, BaseCommitSha: { }, StartCommitSha: { } } latest, ..]
                ? Result<GitLabMergeRequestVersionDto>.Ok(latest)
                : Result<GitLabMergeRequestVersionDto>.Fail(CommentPostErrors.Permanent(
                    $"Merge request {slug}!{mrIid} has no diff version to comment on."));
        }
    }

    private async Task<Result<ProviderCommentId>> PostDiscussionAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string mergeRequestPath,
        int mrIid,
        GitLabMergeRequestVersionDto version,
        InlineComment comment,
        string token,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            new Uri(EnsureTrailingSlash(apiBaseUrl), $"{mergeRequestPath}/discussions"));
        AddCommonHeaders(request, token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(
                new GitLabDiscussionWriteDto(
                    comment.Body,
                    new GitLabPositionWriteDto(
                        "text",
                        version.BaseCommitSha!,
                        version.StartCommitSha!,
                        version.HeadCommitSha!,
                        comment.Path,
                        comment.Line)),
                JsonOptions),
            Encoding.UTF8,
            "application/json");

        string action = $"Posting a discussion on {slug}!{mrIid} at {comment.Path}:{comment.Line}";
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (CommentPostErrors.IsTransportFailure(ex, cancellationToken))
        {
            return Result<ProviderCommentId>.Fail(CommentPostErrors.Transient($"{action} did not complete: {ex.GetType().Name}."));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Result<ProviderCommentId>.Fail(ClassifyReviewFailure(response, action));
            }

            RecordRestHeadroom(response);

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            GitLabDiscussionDto? created = JsonSerializer.Deserialize<GitLabDiscussionDto>(body, JsonOptions);

            return ProviderCommentId.FromProvider(created?.Notes is [var first, ..] ? first.Id : null) is { } id
                ? Result<ProviderCommentId>.Ok(id)
                : Result<ProviderCommentId>.Fail(CommentPostErrors.Permanent($"{action}: GitLab returned no note ID."));
        }
    }

    private static Error ClassifyReviewFailure(HttpResponseMessage response, string action)
    {
        string message = $"{action} returned {(int)response.StatusCode}.";
        return IsRetryableStatus(response) ? CommentPostErrors.Transient(message) : CommentPostErrors.Permanent(message);
    }

    /// <summary>
    /// Reads a merge request's description (<c>GET projects/:id/merge_requests/:iid</c>). A missing or null
    /// description reads as empty.
    /// </summary>
    public async Task<Result<string>> GetMergeRequestDescriptionAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string mergeRequestUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<string>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        if (!TryParseMrIid(mergeRequestUrl, out int mrIid))
        {
            return Result<string>.Fail(GitLabErrors.InvalidMergeRequestUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"projects/{encodedPath}/merge_requests/{mrIid}");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<string>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabMergeRequestDescriptionDto? mergeRequest =
            JsonSerializer.Deserialize<GitLabMergeRequestDescriptionDto>(body, JsonOptions);

        return Result<string>.Ok(mergeRequest?.Description ?? string.Empty);
    }

    private sealed record GitLabMergeRequestVersionDto(string? HeadCommitSha, string? BaseCommitSha, string? StartCommitSha);

    private sealed record GitLabDiscussionWriteDto(string Body, GitLabPositionWriteDto Position);

    private sealed record GitLabPositionWriteDto(
        string PositionType,
        string BaseSha,
        string StartSha,
        string HeadSha,
        string NewPath,
        int NewLine);

    private sealed record GitLabMergeRequestDescriptionDto(string? Description);
}
