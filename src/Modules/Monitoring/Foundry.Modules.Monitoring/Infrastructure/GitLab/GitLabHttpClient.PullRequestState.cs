using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed partial class GitLabHttpClient
{
    public async Task<Result<PullRequestState>> GetPullRequestStateAsync(
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
            Result<MergeRequestSnapshot>.Success success => ToPullRequestState(success.Value),
            Result<MergeRequestSnapshot>.Failure failure => Result<PullRequestState>.Fail(failure.Error),
            _ => throw new InvalidOperationException("Unexpected result."),
        };
    }

    /// <summary>
    /// Maps a snapshot's raw state and <c>sha</c>. The head rule applies here rather than in the snapshot, so a
    /// merge request without a <c>sha</c> still yields its checks.
    /// </summary>
    internal static Result<PullRequestState> ToPullRequestState(MergeRequestSnapshot snapshot)
    {
        return ParsePullRequestState(snapshot.State) switch
        {
            MergeRequestPresence.Merged => Result<PullRequestState>.Ok(new PullRequestState.Merged()),
            MergeRequestPresence.Closed => Result<PullRequestState>.Ok(new PullRequestState.Closed()),
            MergeRequestPresence.Open when CommitSha.TryFrom(snapshot.Status.HeadSha, out CommitSha head) =>
                Result<PullRequestState>.Ok(new PullRequestState.Open(head, snapshot.Status.IsDraft)),
            MergeRequestPresence.Open => Result<PullRequestState>.Fail(PullRequestStateErrors.MissingHead()),
            _ => Result<PullRequestState>.Fail(PullRequestStateErrors.Unrecognised(snapshot.State)),
        };
    }

    /// <summary>
    /// Maps a GitLab merge request <c>state</c>. <c>locked</c> is a merge request in the middle of being merged,
    /// so it is still open. Never returns <see cref="MergeRequestPresence.None"/>; any other value is null.
    /// </summary>
    internal static MergeRequestPresence? ParsePullRequestState(string? state)
    {
        if (string.Equals(state, "opened", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "locked", StringComparison.OrdinalIgnoreCase))
        {
            return MergeRequestPresence.Open;
        }

        if (string.Equals(state, "merged", StringComparison.OrdinalIgnoreCase))
        {
            return MergeRequestPresence.Merged;
        }

        if (string.Equals(state, "closed", StringComparison.OrdinalIgnoreCase))
        {
            return MergeRequestPresence.Closed;
        }

        return null;
    }
}
