using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.PullRequestWatch;

/// <summary>
/// Reads an issue that holds a pull request and returns its <see cref="PullRequestWayOut"/>. Issue closure is checked
/// first because the provider is the authority on it (DOMAIN.md § Provider as Source of Truth for Issue Closure): a
/// closed issue is closed whatever its pull request's state, so its pull request is not read.
/// </summary>
internal static class PullRequestWatch
{
    public static async Task<Result<PullRequestWayOut>> ReadAsync(
        IIssueProvider provider,
        RepositorySlug slug,
        int issueNumber,
        string pullRequestUrl,
        CancellationToken cancellationToken)
    {
        Result<bool> isClosedResult = await provider.IsIssueClosedAsync(slug, issueNumber, cancellationToken);

        return isClosedResult switch
        {
            Result<bool>.Failure failure => Result<PullRequestWayOut>.Fail(failure.Error),
            Result<bool>.Success { Value: true } => new PullRequestWayOut.IssueClosed(),
            _ => await ReadPullRequestAsync(provider, slug, pullRequestUrl, cancellationToken),
        };
    }

    public static async Task<Result<PullRequestWayOut>> ReadAsync(
        IIssueProvider provider,
        RepositorySlug slug,
        IssueStatus issue,
        string pullRequestUrl,
        CancellationToken cancellationToken)
    {
        if (issue.IsClosed)
        {
            return new PullRequestWayOut.IssueClosed();
        }

        return await ReadPullRequestAsync(provider, slug, pullRequestUrl, cancellationToken);
    }

    private static async Task<Result<PullRequestWayOut>> ReadPullRequestAsync(
        IIssueProvider provider,
        RepositorySlug slug,
        string pullRequestUrl,
        CancellationToken cancellationToken)
    {
        Result<PullRequestState> stateResult = await provider.GetPullRequestStateAsync(
            slug,
            pullRequestUrl,
            cancellationToken);

        return stateResult switch
        {
            Result<PullRequestState>.Success { Value: PullRequestState.Closed } => new PullRequestWayOut.PullRequestClosed(),
            Result<PullRequestState>.Success { Value: PullRequestState.Merged } => new PullRequestWayOut.Merged(),
            Result<PullRequestState>.Success { Value: PullRequestState.Open open } =>
                new PullRequestWayOut.Open(open.Head, open.IsDraft),
            Result<PullRequestState>.Failure failure => Result<PullRequestWayOut>.Fail(failure.Error),
            _ => throw new System.Diagnostics.UnreachableException(),
        };
    }
}
