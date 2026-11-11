using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Workers.Contracts;

using FailureCategoryVO = Foundry.Modules.Workers.Contracts.FailureCategory;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// An issue whose triage run failed (ADR 0097): a non-zero exit, a timeout, a cancellation, or a missing or invalid
/// verdict. A transient failure retries automatically; anything else parks under Needs attention until an operator
/// retries or skips triage. Not a <see cref="FailedIssue"/>, whose retry would skip triage.
/// </summary>
public sealed class TriageFailedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private TriageFailedIssue()
    {
    }

    private TriageFailedIssue(IssueId id) : base(id)
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
    {
        TriageFailedIssue failed = new(source.Id);
        failed.CopySharedPropertiesFrom(source);
        failed.SetBlockedBy(source.BlockedBy);
        failed.WorkerRunId = source.WorkerRunId;
        failed.FailureReason = failureReason;
        failed.FailureCategory = failureCategory;
        failed.FailedAt = failedAt;
        return failed;
    }

    /// <summary>Retry triage: the issue waits for a triage worker again.</summary>
    public TriageQueuedIssue RetryTriage()
    {
        TriageQueuedIssue queued = TriageQueuedIssue.FromTriageFailed(this);
        AddDomainEvent(new Events.IssueTriageQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    /// <summary>
    /// Skip triage: the issue goes through the dependency check without a readiness verdict, its kind left as it is.
    /// </summary>
    public Issue SkipTriage() => ReleaseThroughDependencyCheck(verdict: null);
}
