using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class FreshQueuedIssue : QueuedIssue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => true;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private FreshQueuedIssue()
    {
    }

    private FreshQueuedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public override int TierRank => 3;

    public override BranchName? DispatchBranchName => GeneratedBranchName;

    public override DispatchContext Context =>
        new DispatchContext.Fresh(GeneratedBranchName.Value);

    private BranchName GeneratedBranchName =>
        BranchName.Generate(KindClassification, IssueNumber, Title);

    internal static FreshQueuedIssue FromBlocked(BlockedIssue blocked, DateTimeOffset transitionedAt)
    {
        FreshQueuedIssue queued = new(blocked.Id, transitionedAt);
        queued.CopySharedPropertiesFrom(blocked);
        return queued;
    }

    internal static FreshQueuedIssue FromTriage(Issue source, TriageVerdict.Ready? verdict, DateTimeOffset transitionedAt)
    {
        FreshQueuedIssue queued = new(source.Id, transitionedAt);
        queued.CopySharedPropertiesFrom(source);
        if (verdict is not null)
        {
            queued.RecordTriageVerdict(verdict);
        }

        return queued;
    }

    internal static FreshQueuedIssue FromRetry(Issue source, DateTimeOffset transitionedAt)
    {
        FreshQueuedIssue queued = new(source.Id, transitionedAt);
        queued.CopySharedPropertiesFrom(source);
        return queued;
    }

    internal static FreshQueuedIssue FromAwaitingInput(
        AwaitingInputIssue source,
        string input,
        DateTimeOffset answeredAt,
        DateTimeOffset transitionedAt)
    {
        FreshQueuedIssue queued = new(source.Id, transitionedAt);
        queued.CopySharedPropertiesFrom(source);
        queued.AnswerClarification(input, answeredAt);
        return queued;
    }

    public BlockedIssue Block(IReadOnlyList<int> blockers, DateTimeOffset transitionedAt)
    {
        if (blockers.Count == 0)
        {
            throw new InvalidOperationException("Cannot block an issue without specifying at least one blocker.");
        }

        BlockedIssue blocked = BlockedIssue.FromQueued(this, blockers, transitionedAt);
        AddDomainEvent(new Events.IssueBlocked(Id, MonitoredRepositoryId));
        return blocked;
    }

    /// <summary>Re-triage: an operator sends the queued issue back to triage.</summary>
    public TriageQueuedIssue Retriage(DateTimeOffset transitionedAt)
    {
        TriageQueuedIssue queued = TriageQueuedIssue.FromRetriage(this, transitionedAt);
        AddDomainEvent(new Events.IssueTriageQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    public override InProgressIssue Claim(WorkerRunId workerRunId, DateTimeOffset claimedAt)
    {
        InProgressIssue inProgress = InProgressIssue.FromQueued(this, workerRunId, claimedAt);
        AddDomainEvent(new Events.IssueInProgress(Id, MonitoredRepositoryId));
        return inProgress;
    }
}
