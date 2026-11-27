using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal sealed partial class GitHubHttpClient
{
    public async Task<Result<PullRequestState>> GetPullRequestStateAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<PullRequestState>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        if (!TryParsePrNumber(pullRequestUrl, out int prNumber))
        {
            return Result<PullRequestState>.Fail(GitHubErrors.InvalidPullRequestUrl);
        }

        object variables = new { owner = slug.Owner, name = slug.Name, number = prNumber };
        Result<PrStatusData> dataResult = await ExecuteGraphQlAsync<PrStatusData>(
            apiBaseUrl, PrStatusQuery, variables, token, cancellationToken);

        if (dataResult is not Result<PrStatusData>.Success dataSuccess)
        {
            return Result<PullRequestState>.Fail(((Result<PrStatusData>.Failure)dataResult).Error);
        }

        GraphQlPullRequestStatus? pullRequest = dataSuccess.Value.Repository?.PullRequest;
        string? state = pullRequest?.State;

        return ParseGraphQlPullRequestState(state) switch
        {
            MergeRequestPresence.Merged => Result<PullRequestState>.Ok(new PullRequestState.Merged()),
            MergeRequestPresence.Closed => Result<PullRequestState>.Ok(new PullRequestState.Closed()),
            MergeRequestPresence.Open when CommitSha.TryFrom(pullRequest?.HeadRefOid, out CommitSha head) =>
                Result<PullRequestState>.Ok(new PullRequestState.Open(head, pullRequest!.IsDraft)),
            MergeRequestPresence.Open => Result<PullRequestState>.Fail(PullRequestStateErrors.MissingHead()),
            _ => Result<PullRequestState>.Fail(PullRequestStateErrors.Unrecognised(state)),
        };
    }

    /// <summary>
    /// Maps GitHub's GraphQL <c>PullRequestState</c> (<c>OPEN</c>, <c>MERGED</c>, <c>CLOSED</c>). Never returns
    /// <see cref="MergeRequestPresence.None"/>; any other value is null.
    /// </summary>
    internal static MergeRequestPresence? ParseGraphQlPullRequestState(string? state)
    {
        if (string.Equals(state, "OPEN", StringComparison.OrdinalIgnoreCase))
        {
            return MergeRequestPresence.Open;
        }

        if (string.Equals(state, "MERGED", StringComparison.OrdinalIgnoreCase))
        {
            return MergeRequestPresence.Merged;
        }

        if (string.Equals(state, "CLOSED", StringComparison.OrdinalIgnoreCase))
        {
            return MergeRequestPresence.Closed;
        }

        return null;
    }
}
