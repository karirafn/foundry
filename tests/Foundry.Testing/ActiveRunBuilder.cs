using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Testing;

/// <summary>
/// Builds <see cref="ActiveRun"/> instances via the real production path:
/// <see cref="StartingRun.Begin"/> → <see cref="StartingRun.Activate"/>.
/// </summary>
public sealed class ActiveRunBuilder
{
    private IssueId _issueId = IssueId.New();
    private WorkerRunId _workerRunId = WorkerRunId.New();
    private ContainerId _containerId = ContainerId.From("container-test");
    private BranchName _branchName = BranchName.From("feat/1-test");
    private MonitoredRepositoryId _monitoredRepositoryId = MonitoredRepositoryId.New();

    public ActiveRunBuilder WithIssueId(IssueId issueId)
    {
        _issueId = issueId;
        return this;
    }

    public ActiveRunBuilder WithWorkerRunId(WorkerRunId workerRunId)
    {
        _workerRunId = workerRunId;
        return this;
    }

    public ActiveRunBuilder WithContainerId(ContainerId containerId)
    {
        _containerId = containerId;
        return this;
    }

    public ActiveRunBuilder WithBranchName(BranchName branchName)
    {
        _branchName = branchName;
        return this;
    }

    public ActiveRunBuilder WithMonitoredRepositoryId(MonitoredRepositoryId monitoredRepositoryId)
    {
        _monitoredRepositoryId = monitoredRepositoryId;
        return this;
    }

    public ActiveRun Build()
    {
        StartingRun starting = StartingRun.Begin(_issueId, _workerRunId);
        return starting.Activate(_containerId, _branchName, _monitoredRepositoryId);
    }

    /// <summary>
    /// Builds an <see cref="ActiveRun"/> that has had
    /// <see cref="ActiveRun.RecordBranchCommitCount"/> replayed with the given count and SHA,
    /// simulating a run that has already observed a commit.
    /// </summary>
    public ActiveRun WithObservedCommit(int count, string sha)
    {
        ActiveRun run = Build();
        run.RecordBranchCommitCount(count, sha, DateTimeOffset.UtcNow);
        run.ClearDomainEvents();
        return run;
    }
}
