using Foundry.WebApi.Modules.Workers.Domain;

namespace Foundry.WebApi.Modules.Issues.Domain;

public sealed class InProgressIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private InProgressIssue()
    {
    }

    private InProgressIssue(IssueId id) : base(id)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    internal static InProgressIssue FromQueued(QueuedIssue queued, WorkerRunId workerRunId)
    {
        InProgressIssue inProgress = new(queued.Id);
        inProgress.SetSharedProperties(
            queued.MonitoredRepositoryId,
            queued.IssueNumber,
            queued.Title,
            queued.Body,
            queued.Author,
            queued.Url,
            queued.Labels,
            queued.DetectedAt);
        inProgress.WorkerRunId = workerRunId;
        return inProgress;
    }
}
