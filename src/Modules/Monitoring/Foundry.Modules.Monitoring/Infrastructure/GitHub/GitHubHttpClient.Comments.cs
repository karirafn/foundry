using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal sealed partial class GitHubHttpClient
{
    private const int IssueCommentsPerPage = 100;
    private const int MaxIssueCommentPages = 10;

    /// <summary>
    /// Lists an issue's comments updated at or after <paramref name="since"/>
    /// (<c>GET /repos/{owner}/{repo}/issues/{issue_number}/comments</c>), oldest first. GitHub filters
    /// <c>since</c> by update time, so callers still compare each comment's creation time.
    /// </summary>
    public async Task<Result<IReadOnlyList<ProviderComment>>> GetIssueCommentsAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueNumber,
        DateTimeOffset since,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<ProviderComment>>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string sinceParameter = Uri.EscapeDataString(
            since.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        List<ProviderComment> comments = [];

        for (int page = 1; page <= MaxIssueCommentPages; page++)
        {
            string relativePath = $"repos/{owner}/{repo}/issues/{issueNumber}/comments" +
                $"?since={sinceParameter}&per_page={IssueCommentsPerPage}&page={page}";
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(EnsureTrailingSlash(apiBaseUrl), relativePath));
            AddGitHubHeaders(request, token);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result<IReadOnlyList<ProviderComment>>.Fail(ErrorFromNonSuccess(response));
            }

            RecordRestHeadroom(response);

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            List<GitHubIssueCommentDto> pageComments =
                JsonSerializer.Deserialize<List<GitHubIssueCommentDto>>(body, JsonOptions) ?? [];

            foreach (GitHubIssueCommentDto comment in pageComments)
            {
                comments.Add(new ProviderComment(
                    Id: ProviderCommentId.FromProvider(comment.Id),
                    Body: TruncateBody(comment.Body ?? string.Empty),
                    AuthorLogin: comment.User?.Login ?? string.Empty,
                    AuthorIsBot: string.Equals(comment.User?.Type, "Bot", StringComparison.Ordinal),
                    IsSystem: false,
                    CreatedAt: comment.CreatedAt,
                    FilePath: null,
                    Line: null,
                    Origin: CommentOrigin.Conversation,
                    ThreadResolved: false));
            }

            if (pageComments.Count < IssueCommentsPerPage)
            {
                break;
            }
        }

        return Result<IReadOnlyList<ProviderComment>>.Ok(comments);
    }

    /// <summary>
    /// Reads an issue's title and body (<c>GET /repos/{owner}/{repo}/issues/{issue_number}</c>). A missing body is empty.
    /// </summary>
    public async Task<Result<ProviderIssueText>> GetIssueTextAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueNumber,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<ProviderIssueText>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"repos/{owner}/{repo}/issues/{issueNumber}");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddGitHubHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<ProviderIssueText>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubIssueTextDto? issue = JsonSerializer.Deserialize<GitHubIssueTextDto>(body, JsonOptions);

        return Result<ProviderIssueText>.Ok(new ProviderIssueText(issue?.Title ?? string.Empty, issue?.Body ?? string.Empty));
    }

    /// <summary>
    /// Posts a comment to an issue or pull request conversation
    /// (<c>POST /repos/{owner}/{repo}/issues/{issue_number}/comments</c> — GitHub serves pull request
    /// conversations through the issues API) and returns the new comment's ID.
    /// </summary>
    public async Task<Result<ProviderCommentId>> PostIssueCommentAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        CommentThread thread,
        string body,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<ProviderCommentId>.Fail(CommentPostErrors.Permanent(GitHubErrors.InvalidBaseUrl.Message));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Result<ProviderCommentId>.Fail(CommentPostErrors.Permanent("A comment body must not be empty."));
        }

        int? threadNumber = thread switch
        {
            CommentThread.Issue issue => issue.IssueNumber,
            CommentThread.PullRequest pullRequest when TryParsePrNumber(pullRequest.PullRequestUrl, out int prNumber) => prNumber,
            _ => null,
        };

        if (threadNumber is not { } number || number <= 0)
        {
            return Result<ProviderCommentId>.Fail(CommentPostErrors.Permanent(
                $"The comment thread {thread} does not name a valid issue or pull request number."));
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"repos/{owner}/{repo}/issues/{number}/comments");

        using HttpRequestMessage request = new(HttpMethod.Post, requestUri);
        AddGitHubHeaders(request, token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new GitHubCommentWriteDto(body), JsonOptions),
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
                $"Posting a comment on {slug}#{number} did not complete: {ex.GetType().Name}."));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Result<ProviderCommentId>.Fail(ClassifyCommentPostFailure(response, slug, number));
            }

            RecordRestHeadroom(response);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            GitHubIssueCommentDto? created = JsonSerializer.Deserialize<GitHubIssueCommentDto>(responseBody, JsonOptions);

            return ProviderCommentId.FromProvider(created?.Id) is { } id
                ? Result<ProviderCommentId>.Ok(id)
                : Result<ProviderCommentId>.Fail(CommentPostErrors.Permanent(
                    $"GitHub accepted the comment on {slug}#{number} but returned no comment ID."));
        }
    }

    private static Error ClassifyCommentPostFailure(HttpResponseMessage response, RepositorySlug slug, int number)
    {
        int statusCode = (int)response.StatusCode;
        string message = $"Posting a comment on {slug}#{number} returned {statusCode}.";

        bool transient = statusCode >= 500
            || response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
            || (response.StatusCode is HttpStatusCode.Forbidden && IsRateLimited(response));

        return transient ? CommentPostErrors.Transient(message) : CommentPostErrors.Permanent(message);
    }

    private sealed record GitHubIssueCommentDto(
        long? Id,
        string? Body,
        DateTimeOffset CreatedAt,
        GitHubCommentUserDto? User);

    private sealed record GitHubIssueTextDto(string? Title, string? Body);

    private sealed record GitHubCommentUserDto(string? Login, string? Type);

    private sealed record GitHubCommentWriteDto(string Body);
}
