namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// The branch, and the pull request when there is one, that an issue already has while it goes back to triage: a ready
/// verdict resumes the work on them (as a revision with a pull request, as a continuation without one), so the branch
/// name is never recomputed from a changed title or kind.
/// </summary>
public sealed record OpenWork
{
    private OpenWork(string branchName, string? pullRequestUrl)
    {
        BranchName = branchName;
        PullRequestUrl = pullRequestUrl;
    }

    public string BranchName { get; }

    /// <summary>The pull request on <see cref="BranchName"/>, or <see langword="null"/> for branch-only work.</summary>
    public string? PullRequestUrl { get; }

    /// <exception cref="ArgumentException">Either value is empty.</exception>
    public static OpenWork Of(string branchName, string pullRequestUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pullRequestUrl);

        return new OpenWork(branchName, pullRequestUrl);
    }

    /// <summary>Work on a branch that has no pull request yet, such as a queued continuation.</summary>
    /// <exception cref="ArgumentException"><paramref name="branchName"/> is empty.</exception>
    public static OpenWork OnBranch(string branchName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);

        return new OpenWork(branchName, pullRequestUrl: null);
    }
}
