using Foundry.Modules.Issues.Contracts;

namespace Foundry.Modules.Issues.Domain;

public sealed class FailedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private FailedIssue()
    {
    }

    private FailedIssue(IssueId id) : base(id)
    {
    }

    public Guid WorkerRunId { get; private set; }

    public string FailureReason { get; private set; } = string.Empty;

    public DateTimeOffset FailedAt { get; private set; }

    internal static FailedIssue FromInProgress(
        InProgressIssue source,
        Guid workerRunId,
        string failureReason,
        DateTimeOffset failedAt)
    {
        FailedIssue failed = new(source.Id);
        failed.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        failed.WorkerRunId = workerRunId;
        failed.FailureReason = failureReason;
        failed.FailedAt = failedAt;
        return failed;
    }

    public QueuedIssue Retry()
    {
        QueuedIssue queued = QueuedIssue.FromRetry(this);
        AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
