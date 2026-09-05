using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class BlockedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private BlockedIssue()
    {
    }

    private BlockedIssue(IssueId id) : base(id)
    {
    }

    internal static BlockedIssue FromDetected(DetectedIssue detected, IReadOnlyList<int> blockers)
    {
        BlockedIssue blocked = new(detected.Id);
        blocked.SetSharedProperties(
            detected.MonitoredRepositoryId,
            detected.IssueNumber,
            detected.Title,
            detected.Author,
            detected.Url,
            detected.Labels,
            detected.DetectedAt);
        blocked.SetBlockedBy(blockers);
        return blocked;
    }

    internal static BlockedIssue FromQueued(FreshQueuedIssue queued, IReadOnlyList<int> blockers)
    {
        BlockedIssue blocked = new(queued.Id);
        blocked.SetSharedProperties(
            queued.MonitoredRepositoryId,
            queued.IssueNumber,
            queued.Title,
            queued.Author,
            queued.Url,
            queued.Labels,
            queued.DetectedAt);
        blocked.SetBlockedBy(blockers);
        return blocked;
    }

    public FreshQueuedIssue Unblock()
    {
        FreshQueuedIssue queued = FreshQueuedIssue.FromBlocked(this);
        AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
