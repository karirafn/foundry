using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

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
        IssueAuthor author,
        ProviderUrl url,
        IReadOnlyList<string> labels,
        DateTimeOffset detectedAt,
        KindClassification kindClassification)
    {
        DetectedIssue issue = new(IssueId.New());
        issue.SetSharedProperties(
            monitoredRepositoryId,
            issueNumber,
            title,
            author,
            url,
            labels,
            detectedAt,
            kindClassification);
        return issue;
    }

    /// <summary>
    /// Every new issue is triaged before its first implement run, and before the dependency check (ADR 0097).
    /// </summary>
    public TriageQueuedIssue QueueTriage()
    {
        TriageQueuedIssue queued = TriageQueuedIssue.FromDetected(this);
        AddDomainEvent(new Events.IssueTriageQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
