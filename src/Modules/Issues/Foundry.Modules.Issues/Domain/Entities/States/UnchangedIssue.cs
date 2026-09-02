using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;

namespace Foundry.Modules.Issues.Domain.Entities.States;

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

    internal static UnchangedIssue FromInProgress(InProgressIssue source)
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
        unchanged.WorkerRunId = source.WorkerRunId;
        return unchanged;
    }

    public FreshQueuedIssue Retry()
    {
        FreshQueuedIssue queued = FreshQueuedIssue.FromRetry(this);
        AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
