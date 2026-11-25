namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// The branch and pull request an issue already has while it goes back to triage: a ready verdict resumes the work on
/// them as a revision, so the branch name is never recomputed from a changed title or kind.
/// </summary>
public sealed record OpenWork
{
    private OpenWork(string branchName, string pullRequestUrl)
    {
        BranchName = branchName;
        PullRequestUrl = pullRequestUrl;
    }

    public string BranchName { get; }

    public string PullRequestUrl { get; }

    /// <exception cref="ArgumentException">Either value is empty: open work has both a branch and a pull request.</exception>
    public static OpenWork Of(string branchName, string pullRequestUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pullRequestUrl);

        return new OpenWork(branchName, pullRequestUrl);
    }
}
