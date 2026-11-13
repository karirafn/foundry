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

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private TriageQueuedIssue()
    {
    }

    private TriageQueuedIssue(IssueId id) : base(id)
    {
    }

    public override int TierRank => 2;

    /// <summary>A triage run gets no branch.</summary>
    public override BranchName? DispatchBranchName => null;

    public override DispatchContext Context => new DispatchContext.Triage();

    internal static TriageQueuedIssue FromDetected(DetectedIssue source) => CarryingBlockers(source);

    internal static TriageQueuedIssue FromTriageFailed(TriageFailedIssue source) => CarryingBlockers(source);

    internal static TriageQueuedIssue FromTriageBlocked(TriageBlockedIssue source) => CarryingBlockers(source);

    /// <summary>An operator's Re-triage of a queued or blocked issue.</summary>
    internal static TriageQueuedIssue FromRetriage(Issue source) => CarryingBlockers(source);

    internal static TriageQueuedIssue FromAwaitingInput(AwaitingInputIssue source, string input, DateTimeOffset answeredAt)
    {
        TriageQueuedIssue queued = CarryingBlockers(source);
        queued.AnswerClarification(input, answeredAt);
        return queued;
    }

    private static TriageQueuedIssue CarryingBlockers(Issue source)
    {
        TriageQueuedIssue queued = new(source.Id);
        queued.CopySharedPropertiesFrom(source);
        queued.SetBlockedBy(source.BlockedBy);
        return queued;
    }

    public override TriageInProgressIssue Claim(WorkerRunId workerRunId)
    {
        TriageInProgressIssue inProgress = TriageInProgressIssue.FromTriageQueued(this, workerRunId);
        AddDomainEvent(new Events.IssueTriageInProgress(Id, MonitoredRepositoryId));
        return inProgress;
    }

    /// <summary>Open <paramref name="blockers"/> hold the issue untriaged until they close (ADR 0102).</summary>
    public TriageBlockedIssue Block(IReadOnlyList<int> blockers)
    {
        TriageBlockedIssue blocked = TriageBlockedIssue.Untriaged(this, blockers);
        AddDomainEvent(new Events.IssueTriageBlocked(Id, MonitoredRepositoryId));
        return blocked;
    }
}
