using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain.Events;

namespace Foundry.Modules.Workers.Domain;

public sealed class ActiveRun : WorkerRun
{
    // Private parameterless constructor for EF Core materialization.
    private ActiveRun()
    {
    }

    private ActiveRun(
        WorkerRunId id,
        IssueId issueId,
        DateTimeOffset createdAt,
        ContainerId containerId,
        BranchName branchName,
        DateTimeOffset startedAt)
        : base(id, issueId, createdAt)
    {
        ContainerId = containerId;
        BranchName = branchName;
        StartedAt = startedAt;
    }

    public ContainerId ContainerId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public BranchName BranchName { get; private set; }

    internal static ActiveRun FromStarting(StartingRun starting, ContainerId containerId, BranchName branchName)
    {
        return new ActiveRun(
            starting.Id,
            starting.IssueId,
            starting.CreatedAt,
            containerId,
            branchName,
            DateTimeOffset.UtcNow);
    }

    public CompletedRun Complete(int exitCode, BranchName? branchName, PullRequestUrl? pullRequestUrl)
    {
        CompletedRun completed = CompletedRun.FromActive(this, exitCode, branchName, pullRequestUrl);
        AddDomainEvent(new WorkerRunCompleted(Id, IssueId, branchName?.Value, pullRequestUrl?.Value));
        return completed;
    }

    public FailedRun Fail(FailureReason reason, string? containerOutput = null)
    {
        FailedRun failed = FailedRun.FromActive(this, reason, containerOutput);
        AddDomainEvent(new WorkerRunFailed(Id, IssueId, reason.ToString(), BranchName.Value));
        return failed;
    }
}
