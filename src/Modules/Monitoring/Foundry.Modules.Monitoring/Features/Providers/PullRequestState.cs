using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers;

/// <summary>
/// The provider's state of a pull request, read by its URL. A failed or unrecognised read is a
/// <c>Result</c> failure instead; every variant here is a definite answer.
/// </summary>
internal abstract record PullRequestState
{
    private PullRequestState()
    {
    }

    /// <summary>The pull request is open at <paramref name="Head"/>; GitLab's <c>locked</c> (mid-merge) counts as open.</summary>
    public sealed record Open(CommitSha Head, bool IsDraft) : PullRequestState;

    public sealed record Merged : PullRequestState;

    /// <summary>The pull request was closed without being merged.</summary>
    public sealed record Closed : PullRequestState;
}
