using Foundry.Modules.Issues.Contracts;

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
        DateTimeOffset failedAt)
        : base(id, issueId, createdAt)
    {
        Reason = reason;
        FailedAt = failedAt;
    }

    public FailureReason Reason { get; private set; } = null!;

    public DateTimeOffset FailedAt { get; private set; }

    internal static FailedRun FromStarting(StartingRun starting, FailureReason reason)
    {
        return new FailedRun(
            starting.Id,
            starting.IssueId,
            starting.CreatedAt,
            reason,
            DateTimeOffset.UtcNow);
    }

    internal static FailedRun FromActive(ActiveRun active, FailureReason reason)
    {
        return new FailedRun(
            active.Id,
            active.IssueId,
            active.CreatedAt,
            reason,
            DateTimeOffset.UtcNow);
    }
}
