using Foundry.Modules.Issues.Contracts;

namespace Foundry.Modules.Issues.Domain;

public sealed class QueuedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private QueuedIssue()
    {
    }

    private QueuedIssue(IssueId id) : base(id)
    {
    }

    internal static QueuedIssue FromDetected(DetectedIssue detected)
    {
        QueuedIssue queued = new(detected.Id);
        queued.SetSharedProperties(
            detected.MonitoredRepositoryId,
            detected.IssueNumber,
            detected.Title,
            detected.Body,
            detected.Author,
            detected.Url,
            detected.Labels,
            detected.DetectedAt);
        return queued;
    }

    internal static QueuedIssue FromBlocked(BlockedIssue blocked)
    {
        QueuedIssue queued = new(blocked.Id);
        queued.SetSharedProperties(
            blocked.MonitoredRepositoryId,
            blocked.IssueNumber,
            blocked.Title,
            blocked.Body,
            blocked.Author,
            blocked.Url,
            blocked.Labels,
            blocked.DetectedAt);
        return queued;
    }

    public BlockedIssue Block(IReadOnlyList<int> blockers)
    {
        if (blockers.Count == 0)
        {
            throw new InvalidOperationException("Cannot block an issue without specifying at least one blocker.");
        }

        BlockedIssue blocked = BlockedIssue.FromQueued(this, blockers);
        AddDomainEvent(new Events.IssueBlocked(Id, MonitoredRepositoryId));
        return blocked;
    }

    public InProgressIssue Claim(Guid workerRunId)
    {
        return InProgressIssue.FromQueued(this, workerRunId);
    }
}
