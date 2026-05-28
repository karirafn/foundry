using Foundry.WebApi.Modules.Issues.Domain;

namespace Foundry.WebApi.Modules.Workers.Domain;

public sealed class StartingRun : WorkerRun
{
    // Private parameterless constructor for EF Core materialization.
    private StartingRun()
    {
    }

    private StartingRun(WorkerRunId id, IssueId issueId, DateTimeOffset createdAt)
        : base(id, issueId, createdAt)
    {
    }

    public static StartingRun Begin(IssueId issueId, WorkerRunId workerRunId)
    {
        return new StartingRun(workerRunId, issueId, DateTimeOffset.UtcNow);
    }

    public ActiveRun Activate(string containerId)
    {
        ActiveRun active = ActiveRun.FromStarting(this, containerId);
        AddDomainEvent(new WorkerRunStarted(Id, IssueId));
        return active;
    }

    public FailedRun Fail(FailureReason reason)
    {
        FailedRun failed = FailedRun.FromStarting(this, reason);
        AddDomainEvent(new WorkerRunFailed(Id, IssueId, reason.ToString()));
        return failed;
    }
}
