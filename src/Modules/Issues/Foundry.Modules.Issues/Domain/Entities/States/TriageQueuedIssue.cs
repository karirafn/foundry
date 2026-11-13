using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// An issue waiting for a worker to triage it (ADR 0097): every new issue before its first implement run, an issue
/// whose triage questions were answered, an issue an operator sent back to triage, and an untriaged issue whose
/// blockers closed. One that gains open blockers waits for them as a <see cref="TriageBlockedIssue"/> (ADR 0102).
/// </summary>
public sealed class TriageQueuedIssue : QueuedIssue
{
    public override IssueStage Stage => IssueStage.Triage;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => true;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private TriageQueuedIssue()
    {
    }

    private TriageQueuedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public override int TierRank => 2;

    /// <summary>A triage run gets no branch.</summary>
    public override BranchName? DispatchBranchName => null;

    public override DispatchContext Context => new DispatchContext.Triage();

    internal static TriageQueuedIssue FromDetected(DetectedIssue source, DateTimeOffset transitionedAt) =>
        CarryingBlockers(source, transitionedAt);

    internal static TriageQueuedIssue FromTriageFailed(TriageFailedIssue source, DateTimeOffset transitionedAt) =>
        CarryingBlockers(source, transitionedAt);

    internal static TriageQueuedIssue FromTriageBlocked(TriageBlockedIssue source, DateTimeOffset transitionedAt) =>
        CarryingBlockers(source, transitionedAt);

    /// <summary>An operator's Re-triage of a queued or blocked issue.</summary>
    internal static TriageQueuedIssue FromRetriage(Issue source, DateTimeOffset transitionedAt) =>
        CarryingBlockers(source, transitionedAt);

    internal static TriageQueuedIssue FromAwaitingInput(
        AwaitingInputIssue source,
        string input,
        DateTimeOffset answeredAt,
        DateTimeOffset transitionedAt)
    {
        TriageQueuedIssue queued = CarryingBlockers(source, transitionedAt);
        queued.AnswerClarification(input, answeredAt);
        return queued;
    }

    private static TriageQueuedIssue CarryingBlockers(Issue source, DateTimeOffset enteredStateAt)
    {
        TriageQueuedIssue queued = new(source.Id, enteredStateAt);
        queued.CopySharedPropertiesFrom(source);
        queued.SetBlockedBy(source.BlockedBy);
        return queued;
    }

    public override TriageInProgressIssue Claim(WorkerRunId workerRunId, DateTimeOffset claimedAt)
    {
        TriageInProgressIssue inProgress = TriageInProgressIssue.FromTriageQueued(this, workerRunId, claimedAt);
        AddDomainEvent(new Events.IssueTriageInProgress(Id, MonitoredRepositoryId));
        return inProgress;
    }

    /// <summary>Open <paramref name="blockers"/> hold the issue untriaged until they close (ADR 0102).</summary>
    public TriageBlockedIssue Block(IReadOnlyList<int> blockers, DateTimeOffset transitionedAt)
    {
        TriageBlockedIssue blocked = TriageBlockedIssue.Untriaged(this, blockers, transitionedAt);
        AddDomainEvent(new Events.IssueTriageBlocked(Id, MonitoredRepositoryId));
        return blocked;
    }
}
