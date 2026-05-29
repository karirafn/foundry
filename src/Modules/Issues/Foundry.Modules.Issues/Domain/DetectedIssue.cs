using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Issues.Domain;

public sealed class DetectedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private DetectedIssue()
    {
    }

    private DetectedIssue(IssueId id) : base(id)
    {
    }

    public static DetectedIssue Detect(
        MonitoredRepositoryId monitoredRepositoryId,
        int issueNumber,
        string title,
        string body,
        IssueAuthor author,
        ProviderUrl url,
        IReadOnlyList<string> labels,
        DateTimeOffset detectedAt)
    {
        DetectedIssue issue = new(IssueId.New());
        issue.SetSharedProperties(
            monitoredRepositoryId,
            issueNumber,
            title,
            body,
            author,
            url,
            labels,
            detectedAt);
        return issue;
    }

    public QueuedIssue Enqueue()
    {
        if (BlockedBy.Count > 0)
        {
            throw new InvalidOperationException(
                "Cannot enqueue an issue that has unresolved blockers.");
        }

        QueuedIssue queued = QueuedIssue.FromDetected(this);
        AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    public BlockedIssue Block(IReadOnlyList<int> blockers)
    {
        if (blockers.Count == 0)
        {
            throw new InvalidOperationException("Cannot block an issue without specifying at least one blocker.");
        }

        BlockedIssue blocked = BlockedIssue.FromDetected(this, blockers);
        AddDomainEvent(new Events.IssueBlocked(Id, MonitoredRepositoryId));
        return blocked;
    }
}
