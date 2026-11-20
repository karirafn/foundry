using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal sealed partial class GitHubHttpClient
{
    /// <summary>
    /// Posts every inline comment in one review (<c>POST /repos/{owner}/{repo}/pulls/{pull_number}/reviews</c>) with
    /// <c>event: "COMMENT"</c>, on the right-hand side of <paramref name="commitSha"/>'s diff. The call is atomic, and
    /// GitHub returns only the review's ID, so every comment is registered by that ID.
    /// </summary>
    /// <remarks>
    /// <c>COMMENT</c> because Foundry's token opened the pull request, and GitHub's docs do not say whether its author
    /// may approve or request changes on it.
    /// </remarks>
    public async Task<Result<InlineReviewPosting>> PostPullRequestReviewAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        string commitSha,
        string summary,
        IReadOnlyList<InlineComment> comments,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<InlineReviewPosting>.Fail(CommentPostErrors.Permanent(GitHubErrors.InvalidBaseUrl.Message));
        }

        if (!TryParsePrNumber(pullRequestUrl, out int number) || number <= 0)
        {
            return Result<InlineReviewPosting>.Fail(CommentPostErrors.Permanent(
                $"'{pullRequestUrl}' does not name a valid pull request number."));
        }

        if (comments.Count == 0 || string.IsNullOrWhiteSpace(summary))
        {
            return Result<InlineReviewPosting>.Fail(CommentPostErrors.Permanent(
                "A review needs a summary and at least one inline comment."));
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"repos/{owner}/{repo}/pulls/{number}/reviews");

        using HttpRequestMessage request = new(HttpMethod.Post, requestUri);
        AddGitHubHeaders(request, token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(
                new GitHubReviewWriteDto(
                    commitSha,
                    summary,
                    "COMMENT",
                    [.. comments.Select(comment => new GitHubReviewCommentWriteDto(comment.Path, comment.Line, "RIGHT", comment.Body))]),
                JsonOptions),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (CommentPostErrors.IsTransportFailure(ex, cancellationToken))
        {
            return Result<InlineReviewPosting>.Fail(CommentPostErrors.Transient(
                $"Posting a review on {slug}#{number} did not complete: {ex.GetType().Name}."));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string message = $"Posting a review on {slug}#{number} returned {(int)response.StatusCode}.";
                return Result<InlineReviewPosting>.Fail(IsRetryableStatus(response)
                    ? CommentPostErrors.Transient(message)
                    : CommentPostErrors.Permanent(message));
            }

            RecordRestHeadroom(response);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            GitHubReviewDto? created = JsonSerializer.Deserialize<GitHubReviewDto>(responseBody, JsonOptions);

            if (ProviderCommentId.FromProvider(created?.Id) is not { } reviewId)
            {
                return Result<InlineReviewPosting>.Fail(CommentPostErrors.Permanent(
                    $"GitHub accepted the review on {slug}#{number} but returned no review ID."));
            }

            PostedCommentKey key = PostedCommentKey.Review(reviewId);
            return Result<InlineReviewPosting>.Ok(new InlineReviewPosting(
                [.. comments.Select(comment => new PostedInlineComment(comment, key))],
                []));
        }
    }

    /// <summary>
    /// Reads a pull request's description (<c>GET /repos/{owner}/{repo}/pulls/{pull_number}</c>). GitHub answers
    /// <c>body: null</c> for a pull request opened without one, which reads as empty.
    /// </summary>
    public async Task<Result<string>> GetPullRequestBodyAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<string>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        if (!TryParsePrNumber(pullRequestUrl, out int prNumber))
        {
            return Result<string>.Fail(GitHubErrors.InvalidPullRequestUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"repos/{owner}/{repo}/pulls/{prNumber}");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddGitHubHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<string>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubPullRequestBodyDto? pullRequest = JsonSerializer.Deserialize<GitHubPullRequestBodyDto>(body, JsonOptions);

        return Result<string>.Ok(pullRequest?.Body ?? string.Empty);
    }

    private sealed record GitHubReviewWriteDto(
        string CommitId,
        string Body,
        string Event,
        IReadOnlyList<GitHubReviewCommentWriteDto> Comments);

    private sealed record GitHubReviewCommentWriteDto(string Path, int Line, string Side, string Body);

    private sealed record GitHubReviewDto(long? Id);

    private sealed record GitHubPullRequestBodyDto(string? Body);
}
