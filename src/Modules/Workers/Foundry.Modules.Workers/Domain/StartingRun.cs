using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain.Events;

namespace Foundry.Modules.Workers.Domain;

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

    public ActiveRun Activate(
        ContainerId containerId,
        BranchName branchName,
        MonitoredRepositoryId monitoredRepositoryId)
    {
        ActiveRun active = ActiveRun.FromStarting(this, containerId, branchName, monitoredRepositoryId);
        AddDomainEvent(new WorkerRunStarted(Id, IssueId));
        return active;
    }

    public FailedRun Fail(FailureReason reason)
    {
        FailedRun failed = FailedRun.FromStarting(this, reason);
        AddDomainEvent(new WorkerRunFailed(Id, IssueId, reason.ToString(), BranchName: null));
        return failed;
    }
}
