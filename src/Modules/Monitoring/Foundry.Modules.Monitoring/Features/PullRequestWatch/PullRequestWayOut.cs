using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.PullRequestWatch;

/// <summary>
/// The one outcome a <see cref="PullRequestWatch"/> read reaches for an issue that holds a pull request. A failed
/// read is a <c>Result</c> failure instead.
/// </summary>
internal abstract record PullRequestWayOut
{
    private PullRequestWayOut()
    {
    }

    public sealed record IssueClosed : PullRequestWayOut;

    /// <summary>The pull request was closed without being merged.</summary>
    public sealed record PullRequestClosed : PullRequestWayOut;

    public sealed record Merged : PullRequestWayOut;

    public sealed record Open(CommitSha Head, bool IsDraft) : PullRequestWayOut;
}
