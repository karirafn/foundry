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
        DateTimeOffset startedAt)
        : base(id, issueId, createdAt)
    {
        ContainerId = containerId;
        StartedAt = startedAt;
    }

    public ContainerId ContainerId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public string? LatestProgress { get; private set; }

    public BranchName? BranchName { get; private set; }

    internal static ActiveRun FromStarting(StartingRun starting, ContainerId containerId)
    {
        return new ActiveRun(
            starting.Id,
            starting.IssueId,
            starting.CreatedAt,
            containerId,
            DateTimeOffset.UtcNow);
    }

    public void UpdateProgress(string progress)
    {
        LatestProgress = progress;
    }

    public void SetBranchName(BranchName branchName)
    {
        if (BranchName is null)
        {
            BranchName = branchName;
        }
    }

    public CompletedRun Complete(int exitCode, BranchName? branchName, PullRequestUrl? pullRequestUrl)
    {
        CompletedRun completed = CompletedRun.FromActive(this, exitCode, branchName, pullRequestUrl);
        AddDomainEvent(new WorkerRunCompleted(Id, IssueId, branchName?.Value, pullRequestUrl?.Value));
        return completed;
    }

    public FailedRun Fail(FailureReason reason)
    {
        FailedRun failed = FailedRun.FromActive(this, reason);
        AddDomainEvent(new WorkerRunFailed(Id, IssueId, reason.ToString(), BranchName?.Value, LatestProgress));
        return failed;
    }
}
