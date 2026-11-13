using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class BlockedIssue : Issue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => true;

    public override bool WaitsOnBlockers => true;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private BlockedIssue()
    {
    }

    private BlockedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    internal static BlockedIssue FromQueued(FreshQueuedIssue queued, IReadOnlyList<int> blockers, DateTimeOffset transitionedAt)
    {
        BlockedIssue blocked = new(queued.Id, transitionedAt);
        blocked.CopySharedPropertiesFrom(queued);
        blocked.SetBlockedBy(blockers);
        return blocked;
    }

    internal static BlockedIssue FromTriage(Issue source, TriageVerdict.Ready? verdict, DateTimeOffset transitionedAt)
    {
        BlockedIssue blocked = new(source.Id, transitionedAt);
        blocked.CopySharedPropertiesFrom(source);
        blocked.SetBlockedBy(source.BlockedBy);
        if (verdict is not null)
        {
            blocked.RecordTriageVerdict(verdict);
        }

        return blocked;
    }

    /// <summary>Re-triage: an operator sends the blocked issue back to triage; its blockers stay recorded.</summary>
    public TriageQueuedIssue Retriage(DateTimeOffset transitionedAt)
    {
        TriageQueuedIssue queued = TriageQueuedIssue.FromRetriage(this, transitionedAt);
        AddDomainEvent(new Events.IssueTriageQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    public FreshQueuedIssue Unblock(DateTimeOffset transitionedAt)
    {
        FreshQueuedIssue queued = FreshQueuedIssue.FromBlocked(this, transitionedAt);
        AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
