using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using FailureCategoryVO = Foundry.Modules.Workers.Contracts.FailureCategory;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// An issue whose triage failed (ADR 0097): a non-zero exit, a timeout, a cancellation, a missing or invalid
/// verdict, a blocker that would close a dependency cycle, or a blocker the tracker refused to link.
/// A transient failure retries automatically; anything else parks under Needs attention until an operator retries
/// or skips triage.
/// It is not a <see cref="FailedIssue"/>, because retrying a <see cref="FailedIssue"/> skips triage while retrying
/// this one runs triage again.
/// </summary>
public sealed class TriageFailedIssue : Issue, IAutoRetryableFailedState
{
    public override IssueStage Stage => IssueStage.Triage;

    public override bool NeedsAction => true;

    public override bool AwaitsDispatch => false;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private TriageFailedIssue()
    {
    }

    private TriageFailedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public string FailureReason { get; private set; } = string.Empty;

    public FailureCategoryVO FailureCategory { get; private set; } = FailureCategoryVO.NonZeroExit;

    public DateTimeOffset FailedAt { get; private set; }

    internal static TriageFailedIssue FromTriageInProgress(
        TriageInProgressIssue source,
        string failureReason,
        FailureCategoryVO failureCategory,
        DateTimeOffset failedAt)
        => CopyFailure(source, source.WorkerRunId, failureReason, failureCategory, failedAt);

    internal static TriageFailedIssue FromTriageBlocked(
        TriageBlockedIssue source,
        WorkerRunId workerRunId,
        string failureReason,
        FailureCategoryVO failureCategory,
        DateTimeOffset failedAt)
        => CopyFailure(source, workerRunId, failureReason, failureCategory, failedAt);

    private static TriageFailedIssue CopyFailure(
        Issue source,
        WorkerRunId workerRunId,
        string failureReason,
        FailureCategoryVO failureCategory,
        DateTimeOffset failedAt)
    {
        TriageFailedIssue failed = new(source.Id, failedAt);
        failed.CopySharedPropertiesFrom(source);
        failed.SetBlockedBy(source.BlockedBy);
        failed.WorkerRunId = workerRunId;
        failed.FailureReason = failureReason;
        failed.FailureCategory = failureCategory;
        failed.FailedAt = failedAt;
        return failed;
    }

    /// <summary>Retry triage: the issue waits for a triage worker again.</summary>
    public TriageQueuedIssue RetryTriage(DateTimeOffset transitionedAt)
    {
        TriageQueuedIssue queued = TriageQueuedIssue.FromTriageFailed(this, transitionedAt);
        AddDomainEvent(new Events.IssueTriageQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    /// <summary>
    /// Skip triage: the issue goes through the dependency check without a readiness verdict, its kind left as it is.
    /// </summary>
    public Issue SkipTriage(DateTimeOffset transitionedAt) =>
        ReleaseThroughDependencyCheck(verdict: null, transitionedAt);
}
