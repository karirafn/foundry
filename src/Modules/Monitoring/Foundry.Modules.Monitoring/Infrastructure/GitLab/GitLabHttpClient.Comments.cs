using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed partial class GitLabHttpClient
{
    private const int IssueNotesPerPage = 100;
    private const int MaxIssueNotePages = 10;

    /// <summary>
    /// Reads an issue's title and description (<c>GET /projects/:id/issues/:issue_iid</c>). A missing description is empty.
    /// </summary>
    public async Task<Result<ProviderIssueText>> GetIssueTextAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueIid,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<ProviderIssueText>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"projects/{encodedPath}/issues/{issueIid}");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<ProviderIssueText>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabIssueTextDto? issue = JsonSerializer.Deserialize<GitLabIssueTextDto>(body, JsonOptions);

        return Result<ProviderIssueText>.Ok(
            new ProviderIssueText(issue?.Title ?? string.Empty, issue?.Description ?? string.Empty));
    }

    /// <summary>
    /// Lists an issue's notes (<c>GET /projects/:id/issues/:issue_iid/notes</c>), oldest first. GitLab has no
    /// creation-time filter, so callers apply the cutoff.
    /// </summary>
    public async Task<Result<IReadOnlyList<ProviderComment>>> GetIssueNotesAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueIid,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<ProviderComment>>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        List<ProviderComment> comments = [];

        for (int page = 1; page <= MaxIssueNotePages; page++)
        {
            string relativePath = $"projects/{encodedPath}/issues/{issueIid}/notes" +
                $"?sort=asc&order_by=created_at&per_page={IssueNotesPerPage}&page={page}";
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(EnsureTrailingSlash(apiBaseUrl), relativePath));
            AddCommonHeaders(request, token);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result<IReadOnlyList<ProviderComment>>.Fail(ErrorFromNonSuccess(response));
            }

            RecordRestHeadroom(response);

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            List<GitLabNoteDto> notes = JsonSerializer.Deserialize<List<GitLabNoteDto>>(body, JsonOptions) ?? [];

            foreach (GitLabNoteDto note in notes)
            {
                string authorLogin = note.Author?.Username ?? string.Empty;
                comments.Add(new ProviderComment(
                    Id: ProviderCommentId.FromProvider(note.Id),
                    Body: note.Body,
                    AuthorLogin: authorLogin,
                    AuthorIsBot: IsAccessTokenBot(authorLogin),
                    IsSystem: note.System,
                    CreatedAt: note.CreatedAt,
                    FilePath: null,
                    Line: null,
                    Origin: CommentOrigin.Conversation,
                    ThreadResolved: false));
            }

            if (notes.Count < IssueNotesPerPage)
            {
                break;
            }
        }

        return Result<IReadOnlyList<ProviderComment>>.Ok(comments);
    }

    /// <summary>
    /// Posts a note to a merge request (<c>POST /projects/:id/merge_requests/:merge_request_iid/notes</c>) or an
    /// issue (<c>POST /projects/:id/issues/:issue_iid/notes</c>) and returns the new note's ID.
    /// </summary>
    public async Task<Result<ProviderCommentId>> PostNoteAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        CommentThread thread,
        string body,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<ProviderCommentId>.Fail(CommentPostErrors.Permanent(GitLabErrors.InvalidBaseUrl.Message));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Result<ProviderCommentId>.Fail(CommentPostErrors.Permanent("A comment body must not be empty."));
        }

        (string? resource, int iid) = thread switch
        {
            CommentThread.Issue issue => ("issues", issue.IssueNumber),
            CommentThread.PullRequest pullRequest when TryParseMrIid(pullRequest.PullRequestUrl, out int mrIid) =>
                ("merge_requests", mrIid),
            _ => (null, 0),
        };

        if (resource is null || iid <= 0)
        {
            return Result<ProviderCommentId>.Fail(CommentPostErrors.Permanent(
                $"The comment thread {thread} does not name a valid issue or merge request IID."));
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"projects/{encodedPath}/{resource}/{iid}/notes");

        using HttpRequestMessage request = new(HttpMethod.Post, requestUri);
        AddCommonHeaders(request, token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new GitLabNoteWriteDto(body), JsonOptions),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (CommentPostErrors.IsTransportFailure(ex, cancellationToken))
        {
            return Result<ProviderCommentId>.Fail(CommentPostErrors.Transient(
                $"Posting a note on {slug} {resource}/{iid} did not complete: {ex.GetType().Name}."));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Result<ProviderCommentId>.Fail(ClassifyNotePostFailure(response, slug, resource, iid));
            }

            RecordRestHeadroom(response);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            GitLabNoteDto? created = JsonSerializer.Deserialize<GitLabNoteDto>(responseBody, JsonOptions);

            return ProviderCommentId.FromProvider(created?.Id) is { } id
                ? Result<ProviderCommentId>.Ok(id)
                : Result<ProviderCommentId>.Fail(CommentPostErrors.Permanent(
                    $"GitLab accepted the note on {slug} {resource}/{iid} but returned no note ID."));
        }
    }

    // GitLab's notes API carries no bot flag on the author; project and group access tokens act through
    // generated bot users whose usernames follow this pattern.
    internal static bool IsAccessTokenBot(string username) => AccessTokenBotUsername().IsMatch(username);

    private static Error ClassifyNotePostFailure(HttpResponseMessage response, RepositorySlug slug, string resource, int iid)
    {
        string message = $"Posting a note on {slug} {resource}/{iid} returned {(int)response.StatusCode}.";
        return IsRetryableStatus(response) ? CommentPostErrors.Transient(message) : CommentPostErrors.Permanent(message);
    }

    [GeneratedRegex(@"^(project|group)_\d+_bot(_[0-9a-f]+)?\d*$")]
    private static partial Regex AccessTokenBotUsername();

    private sealed record GitLabNoteWriteDto(string Body);

    private sealed record GitLabIssueTextDto(string? Title, string? Description);
}
