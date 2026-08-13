using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.Entities.States;

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
        MonitoredRepositoryId monitoredRepositoryId,
        DateTimeOffset startedAt)
        : base(id, issueId, createdAt)
    {
        ContainerId = containerId;
        BranchName = branchName;
        MonitoredRepositoryId = monitoredRepositoryId;
        StartedAt = startedAt;
    }

    public ContainerId ContainerId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public BranchName BranchName { get; private set; }

    public MonitoredRepositoryId MonitoredRepositoryId { get; private set; }

    public DateTimeOffset? LastActivityAt { get; private set; }

    public string? LastObservedCommitSha { get; private set; }

    public int BranchCommitCount { get; private set; }

    public void RecordActivity(DateTimeOffset observedAt)
    {
        if (LastActivityAt.HasValue && observedAt <= LastActivityAt.Value)
        {
            return;
        }

        LastActivityAt = observedAt;
        AddDomainEvent(new WorkerActivityObserved(Id, IssueId, observedAt, BranchCommitCount));
    }

    public void RecordBranchCommitCount(int count, string? sha, DateTimeOffset observedAt)
    {
        BranchCommitCount = count;

        if (sha == LastObservedCommitSha)
        {
            return;
        }

        LastObservedCommitSha = sha;
        AddDomainEvent(new WorkerActivityObserved(Id, IssueId, observedAt, count));
    }

    internal static ActiveRun FromStarting(
        StartingRun starting,
        ContainerId containerId,
        BranchName branchName,
        MonitoredRepositoryId monitoredRepositoryId)
    {
        return new ActiveRun(
            starting.Id,
            starting.IssueId,
            starting.CreatedAt,
            containerId,
            branchName,
            monitoredRepositoryId,
            DateTimeOffset.UtcNow);
    }

    public CompletedRun Complete(
        int exitCode,
        BranchName? branchName,
        PullRequestUrl? pullRequestUrl,
        RunResultSummary? resultSummary = null)
    {
        CompletedRun completed = CompletedRun.FromActive(this, exitCode, branchName, pullRequestUrl, resultSummary);
        AddDomainEvent(new WorkerRunCompleted(Id, IssueId, branchName?.Value, pullRequestUrl?.Value));
        return completed;
    }

    public FailedRun Fail(
        FailureReason reason,
        BranchName? branchNameOrNull,
        string? containerOutput = null,
        RunResultSummary? resultSummary = null)
    {
        FailedRun failed = FailedRun.FromActive(this, reason, containerOutput, resultSummary);
        AddDomainEvent(new WorkerRunFailed(Id, IssueId, reason.Summary, reason.CategoryToken, branchNameOrNull?.Value));
        return failed;
    }
}
