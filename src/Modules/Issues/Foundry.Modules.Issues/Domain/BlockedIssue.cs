using Foundry.Modules.Issues.Contracts;

namespace Foundry.Modules.Issues.Domain;

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
            detected.Body,
            detected.Author,
            detected.Url,
            detected.Labels,
            detected.DetectedAt);
        blocked.SetBlockedBy(blockers);
        return blocked;
    }

    internal static BlockedIssue FromQueued(QueuedIssue queued, IReadOnlyList<int> blockers)
    {
        BlockedIssue blocked = new(queued.Id);
        blocked.SetSharedProperties(
            queued.MonitoredRepositoryId,
            queued.IssueNumber,
            queued.Title,
            queued.Body,
            queued.Author,
            queued.Url,
            queued.Labels,
            queued.DetectedAt);
        blocked.SetBlockedBy(blockers);
        return blocked;
    }

    internal static BlockedIssue FromIneligible(IneligibleIssue ineligible)
    {
        BlockedIssue blocked = new(ineligible.Id);
        blocked.SetSharedProperties(
            ineligible.MonitoredRepositoryId,
            ineligible.IssueNumber,
            ineligible.Title,
            ineligible.Body,
            ineligible.Author,
            ineligible.Url,
            ineligible.Labels,
            ineligible.DetectedAt);
        blocked.SetBlockedBy(ineligible.BlockedBy);
        return blocked;
    }

    public QueuedIssue Unblock()
    {
        QueuedIssue queued = QueuedIssue.FromBlocked(this);
        AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
