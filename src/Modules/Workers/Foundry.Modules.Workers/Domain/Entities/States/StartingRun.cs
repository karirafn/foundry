using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using WorkerRunFailedEvent = Foundry.Modules.Workers.Domain.Events.WorkerRunFailed;
using WorkerRunStartedEvent = Foundry.Modules.Workers.Domain.Events.WorkerRunStarted;

namespace Foundry.Modules.Workers.Domain.Entities.States;

public sealed class StartingRun : WorkerRun
{
    // Private parameterless constructor for EF Core materialization.
    private StartingRun()
    {
    }

    private StartingRun(WorkerRunId id, IssueId issueId, DateTimeOffset createdAt, WorkerRole role, string? image)
        : base(id, issueId, createdAt, role, image)
    {
    }

    /// <summary>
    /// Begins a run. <paramref name="role"/> defaults to <see cref="WorkerRole.Implement"/>; <paramref name="image"/> is the
    /// worker image the run is dispatched on (see <see cref="WorkerRun.Image"/>).
    /// </summary>
    public static StartingRun Begin(IssueId issueId, WorkerRunId workerRunId, WorkerRole? role = null, string? image = null)
    {
        return new StartingRun(workerRunId, issueId, DateTimeOffset.UtcNow, role ?? WorkerRole.Implement, image);
    }

    public ActiveRun Activate(
        ContainerId containerId,
        BranchName branchName,
        MonitoredRepositoryId monitoredRepositoryId)
    {
        ActiveRun active = ActiveRun.FromStarting(this, containerId, branchName, monitoredRepositoryId);
        AddDomainEvent(new WorkerRunStartedEvent(Id, IssueId));
        return active;
    }

    public FailedRun Fail(FailureReason reason)
    {
        FailedRun failed = FailedRun.FromStarting(this, reason);
        AddDomainEvent(new WorkerRunFailedEvent(Id, IssueId, reason.Summary, reason.CategoryToken, BranchName: null));
        return failed;
    }
}
