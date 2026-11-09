using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.Entities.States;

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
        RunResultSummary? resultSummary,
        WorkerRole role,
        string? image)
        : base(id, issueId, createdAt, role, image)
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

    /// <summary>
    /// The redacted, Docker-timestamped container log tail, kept so a slow or stalled run can be
    /// diagnosed after its container is removed.
    /// </summary>
    public string? ContainerOutput { get; private set; }

    public RunResultSummary? ResultSummary { get; private set; }

    /// <summary>
    /// Carried over from <see cref="ActiveRun.CancelRequestedAt"/> so a run that resolved
    /// before a pending cancel took effect can be told apart.
    /// </summary>
    public DateTimeOffset? CancelRequestedAt { get; private set; }

    internal static CompletedRun FromActive(
        ActiveRun active,
        int exitCode,
        BranchName? branchName,
        PullRequestUrl? pullRequestUrl,
        RunResultSummary? resultSummary = null,
        RunVerdict? verdict = null,
        string? containerOutput = null)
    {
        CompletedRun completed = new(
            active.Id,
            active.IssueId,
            active.CreatedAt,
            exitCode,
            DateTimeOffset.UtcNow,
            branchName,
            pullRequestUrl,
            resultSummary,
            active.Role,
            active.Image)
        {
            CancelRequestedAt = active.CancelRequestedAt,
            ContainerOutput = containerOutput,
        };

        completed.RecordVerdict(verdict);
        return completed;
    }
}
