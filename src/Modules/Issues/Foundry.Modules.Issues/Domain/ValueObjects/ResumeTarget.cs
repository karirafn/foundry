namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// Where the work resumes once input arrives (ADR 0096), recorded when the issue enters awaiting input from what the
/// asking run left behind.
/// </summary>
public abstract record ResumeTarget
{
    private ResumeTarget()
    {
    }

    /// <summary>The run left an open pull request: input queues a revision of it.</summary>
    public sealed record Revision(string BranchName, string PullRequestUrl) : ResumeTarget;

    /// <summary>The run left commits but no pull request: input queues a continuation on its branch.</summary>
    public sealed record Continuation(string BranchName) : ResumeTarget;

    /// <summary>The run pushed nothing: input queues a fresh run.</summary>
    public sealed record Fresh : ResumeTarget;

    /// <summary>Triage asked before the first implement run (ADR 0097): input queues the issue for triage again.</summary>
    public sealed record Retriage : ResumeTarget;

    /// <summary>Derives the target from the facts at the asking run's exit.</summary>
    public static ResumeTarget FromRunFacts(string branchName, string? pullRequestUrl, bool branchHasCommits) =>
        pullRequestUrl is not null
            ? new Revision(branchName, pullRequestUrl)
            : branchHasCommits
                ? new Continuation(branchName)
                : new Fresh();
}
