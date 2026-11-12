using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>What the Publisher's Publish mode reported for a run (ADR 0100).</summary>
public abstract record PublishResult
{
    private PublishResult()
    {
    }

    /// <summary>The run's commits are on its branch, whose head is now <paramref name="Head"/>.</summary>
    /// <param name="BranchCommitCount">The commits the branch holds beyond the default branch.</param>
    /// <param name="CommitSubjects">The subjects of the run's commits as pushed, oldest first.</param>
    public sealed record Published(CommitSha Head, int BranchCommitCount, IReadOnlyList<string> CommitSubjects) : PublishResult;

    /// <summary>The run produced no commits: no Run bundle, or an empty one.</summary>
    public sealed record NothingToPublish : PublishResult;

    /// <summary>
    /// Someone pushed to the run's branch during the run and merging the run's work conflicted, so the run's work went
    /// to <paramref name="SideBranch"/> instead.
    /// </summary>
    public sealed record Diverged(BranchName Branch, BranchName SideBranch, CommitSha Head) : PublishResult;

    /// <summary>
    /// Publishing failed. A <paramref name="Transient"/> failure (network, provider outage) is retried with backoff;
    /// a permanent one (an invalid bundle, a refused token) is not.
    /// </summary>
    public sealed record Failed(string Reason, bool Transient) : PublishResult;
}
