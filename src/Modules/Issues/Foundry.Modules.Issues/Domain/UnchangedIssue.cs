using Foundry.Modules.Issues.Contracts;

namespace Foundry.Modules.Issues.Domain;

public sealed class UnchangedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private UnchangedIssue()
    {
    }

    private UnchangedIssue(IssueId id) : base(id)
    {
    }

    public Guid WorkerRunId { get; private set; }

    internal static UnchangedIssue FromInProgress(InProgressIssue source, Guid workerRunId)
    {
        UnchangedIssue unchanged = new(source.Id);
        unchanged.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        unchanged.WorkerRunId = workerRunId;
        return unchanged;
    }

    public QueuedIssue Retry()
    {
        QueuedIssue queued = QueuedIssue.FromRetry(this);
        AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    public CompletedIssue Complete(DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromUnchanged(this, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }
}
