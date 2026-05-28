using Foundry.WebApi.Modules.Monitoring.Domain;

namespace Foundry.WebApi.Modules.Issues.Domain;

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
}
