using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// An issue a triage worker is reading (ADR 0097). It holds a worker slot like any running worker, and leaves by its
/// run's verdict: <c>ready</c> through the dependency check, <c>needs_input</c> to awaiting input, <c>blocked</c> to
/// <see cref="TriageBlockedIssue"/>, or a failed run to <see cref="TriageFailedIssue"/>.
/// </summary>
public sealed class TriageInProgressIssue : Issue
{
    public override IssueStage Stage => IssueStage.Triage;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => false;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private TriageInProgressIssue()
    {
    }

    private TriageInProgressIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    internal static TriageInProgressIssue FromTriageQueued(
        TriageQueuedIssue source,
        WorkerRunId workerRunId,
        DateTimeOffset claimedAt)
    {
        TriageInProgressIssue inProgress = new(source.Id, claimedAt);
        inProgress.CopySharedPropertiesFrom(source);
        inProgress.SetBlockedBy(source.BlockedBy);
        inProgress.WorkerRunId = workerRunId;
        return inProgress;
    }

    /// <summary>
    /// Triage judged the issue ready: it records the verdict and goes through the dependency check, queued when it has
    /// no open blockers and blocked otherwise.
    /// </summary>
    public Issue MarkReady(TriageVerdict.Ready verdict, DateTimeOffset transitionedAt)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return ReleaseThroughDependencyCheck(verdict, transitionedAt);
    }

    /// <summary>Triage needs answers first: it records the verdict and waits for input, then triages again.</summary>
    public AwaitingInputIssue MarkNeedsInput(TriageVerdict.NeedsInput verdict, DateTimeOffset transitionedAt)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        AwaitingInputIssue awaiting = AwaitingInputIssue.FromTriageInProgress(this, verdict, transitionedAt);
        AddDomainEvent(new Events.IssueAwaitingInput(Id, MonitoredRepositoryId, IssueNumber));
        return awaiting;
    }

    /// <summary>
    /// Triage found open issues that must land first: it records the verdict and waits untriaged on the blockers it
    /// named, then on any already recorded.
    /// </summary>
    public TriageBlockedIssue MarkBlocked(TriageVerdict.Blocked verdict, DateTimeOffset transitionedAt)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        TriageBlockedIssue blocked = TriageBlockedIssue.FromTriageInProgress(this, verdict, transitionedAt);
        AddDomainEvent(new Events.IssueTriageBlocked(Id, MonitoredRepositoryId));
        AddDomainEvent(new Events.TriageBlockersNamed(
            Id,
            MonitoredRepositoryId,
            IssueNumber,
            WorkerRunId,
            verdict.BlockedBy));
        return blocked;
    }

    /// <summary>
    /// A blocker triage named would close a dependency cycle: triage fails, so no link is written and the person
    /// removes one of the cycle's links before retrying.
    /// </summary>
    public TriageFailedIssue FailOnBlockerCycle(DependencyCycle cycle, DateTimeOffset failedAt)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        string failureReason =
            $"Linking #{cycle.ClosingBlocker} as a blocker would close a dependency cycle: {cycle.Describe()}, "
            + "each blocked by the next. Remove one of these links, then retry triage.";
        return MarkFailed(failureReason, FailureCategory.BlockerCycle, failedAt);
    }

    public TriageFailedIssue MarkFailed(string failureReason, FailureCategory failureCategory, DateTimeOffset failedAt)
    {
        TriageFailedIssue failed = TriageFailedIssue.FromTriageInProgress(this, failureReason, failureCategory, failedAt);
        AddDomainEvent(new Events.IssueTriageFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
