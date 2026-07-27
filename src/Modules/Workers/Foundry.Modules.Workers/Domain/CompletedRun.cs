using Foundry.Modules.Issues.Contracts;
using Foundry.Shared;
using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Domain;

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
        PullRequestUrl? pullRequestUrl,
        RunResultSummary? resultSummary)
        : base(id, issueId, createdAt)
    {
        ExitCode = exitCode;
        CompletedAt = completedAt;
        BranchName = branchName;
        PullRequestUrl = pullRequestUrl;
        ResultSummary = resultSummary;
    }

    public int ExitCode { get; private set; }

    public DateTimeOffset CompletedAt { get; private set; }

    public BranchName? BranchName { get; private set; }

    public PullRequestUrl? PullRequestUrl { get; private set; }

    public RunResultSummary? ResultSummary { get; private set; }

    internal static CompletedRun FromActive(
        ActiveRun active,
        int exitCode,
        BranchName? branchName,
        PullRequestUrl? pullRequestUrl,
        RunResultSummary? resultSummary = null)
    {
        return new CompletedRun(
            active.Id,
            active.IssueId,
            active.CreatedAt,
            exitCode,
            DateTimeOffset.UtcNow,
            branchName,
            pullRequestUrl,
            resultSummary);
    }
}
