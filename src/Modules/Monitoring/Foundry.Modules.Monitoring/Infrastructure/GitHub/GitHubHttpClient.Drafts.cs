using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

/// <summary>
/// Marks a draft pull request ready for review. GitHub's REST update endpoint has no draft field, so this uses
/// the GraphQL mutation <c>markPullRequestReadyForReview</c>, whose input is the pull request's node id
/// (https://docs.github.com/en/graphql/reference/mutations#markpullrequestreadyforreview).
/// </summary>
internal sealed partial class GitHubHttpClient
{
    private const string MarkPullRequestReadyMutation = """
        mutation MarkPullRequestReady($pullRequestId: ID!) {
          markPullRequestReadyForReview(input: { pullRequestId: $pullRequestId }) {
            pullRequest { isDraft }
          }
        }
        """;

    /// <summary>
    /// Reads the pull request's node id and draft state with the status query, then sends the mutation when it
    /// is a draft. A pull request that is already ready needs no mutation and succeeds.
    /// </summary>
    public async Task<Result> MarkPullRequestReadyAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result.Fail(RunPublishingErrors.Permanent(GitHubErrors.InvalidBaseUrl.Message));
        }

        if (!TryParsePrNumber(pullRequestUrl, out int prNumber))
        {
            return Result.Fail(RunPublishingErrors.Permanent(GitHubErrors.InvalidPullRequestUrl.Message));
        }

        string action = $"Marking pull request #{prNumber} on {slug} ready for review";
        Error ClassifyNonSuccess(HttpResponseMessage response) => ClassifyPublishingFailure(response, action);

        try
        {
            Result<PrStatusData> read = await ExecuteGraphQlAsync<PrStatusData>(
                apiBaseUrl,
                PrStatusQuery,
                new { owner = slug.Owner, name = slug.Name, number = prNumber },
                token,
                cancellationToken,
                ClassifyNonSuccess);

            if (read is not Result<PrStatusData>.Success readSuccess)
            {
                return Result.Fail(ToPublishingError(((Result<PrStatusData>.Failure)read).Error, action));
            }

            GraphQlPullRequestStatus? pullRequest = readSuccess.Value.Repository?.PullRequest;
            if (string.IsNullOrWhiteSpace(pullRequest?.Id))
            {
                return Result.Fail(RunPublishingErrors.Permanent($"{action}: GitHub returned no pull request."));
            }

            if (!pullRequest.IsDraft)
            {
                return Result.Ok();
            }

            Result<MarkReadyData> marked = await ExecuteGraphQlAsync<MarkReadyData>(
                apiBaseUrl,
                MarkPullRequestReadyMutation,
                new { pullRequestId = pullRequest.Id },
                token,
                cancellationToken,
                ClassifyNonSuccess);

            return marked is Result<MarkReadyData>.Failure failure
                ? Result.Fail(ToPublishingError(failure.Error, action))
                : Result.Ok();
        }
        catch (Exception ex) when (CommentPostErrors.IsTransportFailure(ex, cancellationToken))
        {
            return Result.Fail(RunPublishingErrors.Transient($"{action} did not complete: {ex.GetType().Name}."));
        }
    }

    // ExecuteGraphQlAsync already classifies HTTP failures through ClassifyPublishingFailure; what remains are
    // GraphQL-level errors, where only RATE_LIMITED may succeed on retry.
    private static Error ToPublishingError(Error error, string action)
    {
        if (error.Code is RunPublishingErrors.TransientCode or RunPublishingErrors.PermanentCode)
        {
            return error;
        }

        string message = $"{action} failed: {error.Message}";
        return error.Code == GitHubErrors.RateLimitExhausted.Code
            ? RunPublishingErrors.Transient(message)
            : RunPublishingErrors.Permanent(message);
    }

    private sealed record MarkReadyData(MarkReadyPayload? MarkPullRequestReadyForReview);

    private sealed record MarkReadyPayload(MarkReadyPullRequest? PullRequest);

    private sealed record MarkReadyPullRequest(bool IsDraft);
}
