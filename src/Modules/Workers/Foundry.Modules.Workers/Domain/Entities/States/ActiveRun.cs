using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;

using WorkerActivityObservedEvent = Foundry.Modules.Workers.Domain.Events.WorkerActivityObserved;
using WorkerRunCompletedEvent = Foundry.Modules.Workers.Domain.Events.WorkerRunCompleted;
using WorkerRunFailedEvent = Foundry.Modules.Workers.Domain.Events.WorkerRunFailed;

namespace Foundry.Modules.Workers.Domain.Entities.States;

public sealed class ActiveRun : WorkerRun, ITerminableRun
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
        BranchName? branchName,
        MonitoredRepositoryId monitoredRepositoryId,
        DateTimeOffset startedAt,
        WorkerRole role,
        string? image)
        : base(id, issueId, createdAt, role, image)
    {
        ContainerId = containerId;
        BranchName = branchName;
        MonitoredRepositoryId = monitoredRepositoryId;
        StartedAt = startedAt;
    }

    public ContainerId ContainerId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>The branch the run works on, or <see langword="null"/> for a run that gets none (a restricted role).</summary>
    public BranchName? BranchName { get; private set; }

    public MonitoredRepositoryId MonitoredRepositoryId { get; private set; }

    public DateTimeOffset? LastActivityAt { get; private set; }

    /// <summary>
    /// When a user asked to cancel this run. The endpoint only records the request;
    /// <c>WorkerDispatchService</c> remains the single writer of run state and applies it.
    /// </summary>
    public DateTimeOffset? CancelRequestedAt { get; private set; }

    /// <summary>
    /// Records a cancel request. Idempotent: a second call keeps the first timestamp.
    /// </summary>
    public void RequestCancel(DateTimeOffset requestedAt)
    {
        if (CancelRequestedAt.HasValue)
        {
            return;
        }

        CancelRequestedAt = requestedAt;
    }

    public void RecordActivity(DateTimeOffset observedAt)
    {
        if (LastActivityAt.HasValue && observedAt <= LastActivityAt.Value)
        {
            return;
        }

        LastActivityAt = observedAt;
        AddDomainEvent(new WorkerActivityObservedEvent(Id, IssueId, observedAt));
    }

    internal static ActiveRun FromStarting(
        StartingRun starting,
        ContainerId containerId,
        BranchName? branchName,
        MonitoredRepositoryId monitoredRepositoryId)
    {
        ActiveRun active = new(
            starting.Id,
            starting.IssueId,
            starting.CreatedAt,
            containerId,
            branchName,
            monitoredRepositoryId,
            DateTimeOffset.UtcNow,
            starting.Role,
            starting.Image);
        active.CarryDispatchFactsFrom(starting);
        return active;
    }

    /// <summary>
    /// The container ended: Foundry publishes the run's commits before resolving its outcome (ADR 0100). Only a run
    /// with a branch has commits to publish.
    /// </summary>
    public PublishingRun BeginPublishing(
        RunExit exit,
        string? containerOutput,
        RunVerdict? verdict,
        RunPullRequestText? pullRequestText,
        WorkerRunTranscript? transcript = null)
    {
        ArgumentNullException.ThrowIfNull(exit);

        if (BranchName is not { } branchName)
        {
            throw new InvalidOperationException($"Worker run '{Id.Value}' has no branch, so it has nothing to publish.");
        }

        return PublishingRun.FromActive(this, branchName, exit, containerOutput, verdict, pullRequestText, transcript);
    }

    public CompletedRun Complete(
        int exitCode,
        BranchName? branchName,
        PullRequestUrl? pullRequestUrl,
        RunResultSummary? resultSummary = null,
        RunVerdict? verdict = null,
        string? containerOutput = null)
    {
        CompletedRun completed = CompletedRun.FromActive(
            this,
            exitCode,
            branchName,
            pullRequestUrl,
            resultSummary,
            verdict,
            containerOutput);
        AddDomainEvent(new WorkerRunCompletedEvent(Id, IssueId, branchName?.Value, pullRequestUrl?.Value));
        return completed;
    }

    public FailedRun Fail(
        FailureReason reason,
        BranchName? branchNameOrNull,
        string? containerOutput = null,
        RunResultSummary? resultSummary = null,
        RunVerdict? verdict = null)
    {
        FailedRun failed = FailedRun.FromActive(this, reason, containerOutput, resultSummary, verdict);
        AddDomainEvent(new WorkerRunFailedEvent(Id, IssueId, reason.Summary, reason.CategoryToken, branchNameOrNull?.Value));
        return failed;
    }
}
