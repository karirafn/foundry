using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class BlockedIssue : Issue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => true;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private BlockedIssue()
    {
    }

    private BlockedIssue(IssueId id) : base(id)
    {
    }

    internal static BlockedIssue FromQueued(FreshQueuedIssue queued, IReadOnlyList<int> blockers)
    {
        BlockedIssue blocked = new(queued.Id);
        blocked.CopySharedPropertiesFrom(queued);
        blocked.SetBlockedBy(blockers);
        return blocked;
    }

    internal static BlockedIssue FromTriage(Issue source, TriageVerdict.Ready? verdict)
    {
        BlockedIssue blocked = new(source.Id);
        blocked.CopySharedPropertiesFrom(source);
        blocked.SetBlockedBy(source.BlockedBy);
        if (verdict is not null)
        {
            blocked.RecordTriageVerdict(verdict);
        }

        return blocked;
    }

    /// <summary>Re-triage: an operator sends the blocked issue back to triage; its blockers stay recorded.</summary>
    public TriageQueuedIssue Retriage()
    {
        TriageQueuedIssue queued = TriageQueuedIssue.FromRetriage(this);
        AddDomainEvent(new Events.IssueTriageQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    public FreshQueuedIssue Unblock()
    {
        FreshQueuedIssue queued = FreshQueuedIssue.FromBlocked(this);
        AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
