using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.Entities.States;

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
        RunResultSummary? resultSummary,
        WorkerRole role,
        string? image)
        : base(id, issueId, createdAt, role, image)
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

    /// <summary>
    /// Carried over from <see cref="ActiveRun.CancelRequestedAt"/> so a run that resolved
    /// before a pending cancel took effect can be told apart.
    /// </summary>
    public DateTimeOffset? CancelRequestedAt { get; private set; }

    internal static FailedRun FromStarting(StartingRun starting, FailureReason reason)
    {
        FailedRun failed = new(
            starting.Id,
            starting.IssueId,
            starting.CreatedAt,
            reason,
            DateTimeOffset.UtcNow,
            branchName: null,
            containerOutput: null,
            resultSummary: null,
            starting.Role,
            starting.Image);
        failed.CarryDispatchFactsFrom(starting);
        return failed;
    }

    internal static FailedRun FromUnpublished(
        UnpublishedRun unpublished,
        FailureReason reason,
        string? containerOutput,
        RunResultSummary? resultSummary,
        RunVerdict? verdict)
    {
        FailedRun failed = new(
            unpublished.Id,
            unpublished.IssueId,
            unpublished.CreatedAt,
            reason,
            DateTimeOffset.UtcNow,
            unpublished.BranchName,
            containerOutput,
            resultSummary,
            unpublished.Role,
            unpublished.Image)
        {
            CancelRequestedAt = unpublished.CancelRequestedAt,
        };

        failed.RecordVerdict(verdict);
        failed.CarryDispatchFactsFrom(unpublished);
        failed.RecordPublishFacts(
            unpublished.PublishedHead,
            unpublished.PublishedBranchCommitCount,
            unpublished.DivergedSideBranch,
            unpublished.RevertedFiles);
        return failed;
    }

    internal static FailedRun FromActive(
        ActiveRun active,
        FailureReason reason,
        string? containerOutput = null,
        RunResultSummary? resultSummary = null,
        RunVerdict? verdict = null)
    {
        FailedRun failed = new(
            active.Id,
            active.IssueId,
            active.CreatedAt,
            reason,
            DateTimeOffset.UtcNow,
            active.BranchName,
            containerOutput,
            resultSummary,
            active.Role,
            active.Image)
        {
            CancelRequestedAt = active.CancelRequestedAt,
        };

        failed.RecordVerdict(verdict);
        failed.CarryDispatchFactsFrom(active);
        return failed;
    }
}
