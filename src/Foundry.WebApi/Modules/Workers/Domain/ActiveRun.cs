using Foundry.WebApi.Modules.Issues.Domain;

namespace Foundry.WebApi.Modules.Workers.Domain;

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

    public CompletedRun Complete(int exitCode, string? branchName, string? pullRequestUrl)
    {
        CompletedRun completed = CompletedRun.FromActive(this, exitCode, branchName, pullRequestUrl);
        AddDomainEvent(new WorkerRunCompleted(Id, IssueId, branchName, pullRequestUrl));
        return completed;
    }

    public FailedRun Fail(FailureReason reason)
    {
        FailedRun failed = FailedRun.FromActive(this, reason);
        AddDomainEvent(new WorkerRunFailed(Id, IssueId, reason.ToString()));
        return failed;
    }
}
