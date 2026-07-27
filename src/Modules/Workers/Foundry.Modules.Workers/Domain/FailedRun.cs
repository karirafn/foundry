using Foundry.Modules.Issues.Contracts;
using Foundry.Shared;
using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Domain;

public sealed class FailedRun : WorkerRun
{
    // Private parameterless constructor for EF Core materialization.
    private FailedRun()
    {
    }

    private FailedRun(
        WorkerRunId id,
        IssueId issueId,
        DateTimeOffset createdAt,
        FailureReason reason,
        DateTimeOffset failedAt,
        BranchName? branchName,
        string? containerOutput,
        RunResultSummary? resultSummary)
        : base(id, issueId, createdAt)
    {
        Reason = reason;
        FailedAt = failedAt;
        BranchName = branchName;
        ContainerOutput = containerOutput;
        ResultSummary = resultSummary;
    }

    public FailureReason Reason { get; private set; } = null!;

    public DateTimeOffset FailedAt { get; private set; }

    public BranchName? BranchName { get; private set; }

    public string? ContainerOutput { get; private set; }

    public RunResultSummary? ResultSummary { get; private set; }

    internal static FailedRun FromStarting(StartingRun starting, FailureReason reason)
    {
        return new FailedRun(
            starting.Id,
            starting.IssueId,
            starting.CreatedAt,
            reason,
            DateTimeOffset.UtcNow,
            branchName: null,
            containerOutput: null,
            resultSummary: null);
    }

    internal static FailedRun FromActive(
        ActiveRun active,
        FailureReason reason,
        string? containerOutput = null,
        RunResultSummary? resultSummary = null)
    {
        return new FailedRun(
            active.Id,
            active.IssueId,
            active.CreatedAt,
            reason,
            DateTimeOffset.UtcNow,
            active.BranchName,
            containerOutput,
            resultSummary);
    }
}
