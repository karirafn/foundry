using Foundry.WebApi.Modules.Issues.Domain;

namespace Foundry.WebApi.Modules.Workers.Domain;

public sealed class CompletedRun : WorkerRun
{
    // Private parameterless constructor for EF Core materialization.
    private CompletedRun()
    {
    }

    private CompletedRun(
        WorkerRunId id,
        IssueId issueId,
        DateTimeOffset createdAt,
        int exitCode,
        DateTimeOffset completedAt,
        BranchName? branchName,
        PullRequestUrl? pullRequestUrl)
        : base(id, issueId, createdAt)
    {
        ExitCode = exitCode;
        CompletedAt = completedAt;
        BranchName = branchName;
        PullRequestUrl = pullRequestUrl;
    }

    public int ExitCode { get; private set; }

    public DateTimeOffset CompletedAt { get; private set; }

    public BranchName? BranchName { get; private set; }

    public PullRequestUrl? PullRequestUrl { get; private set; }

    internal static CompletedRun FromActive(ActiveRun active, int exitCode, BranchName? branchName, PullRequestUrl? pullRequestUrl)
    {
        return new CompletedRun(
            active.Id,
            active.IssueId,
            active.CreatedAt,
            exitCode,
            DateTimeOffset.UtcNow,
            branchName,
            pullRequestUrl);
    }
}
