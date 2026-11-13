using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class DetectedIssue : Issue
{
    public override IssueStage Stage => IssueStage.Triage;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => true;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private DetectedIssue()
    {
    }

    private DetectedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
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
        DetectedIssue issue = new(IssueId.New(), detectedAt);
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
    /// Every new issue is triaged before its first implement run (ADR 0097), but not before its blockers close: an
    /// issue with open blockers waits untriaged, so triage judges it against the code they wrote (ADR 0102).
    /// </summary>
    public TriageQueuedIssue QueueTriage(DateTimeOffset transitionedAt)
    {
        TriageQueuedIssue queued = TriageQueuedIssue.FromDetected(this, transitionedAt);
        AddDomainEvent(new Events.IssueTriageQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    /// <summary>A new issue with open <paramref name="blockers"/> waits untriaged until they close (ADR 0102).</summary>
    public TriageBlockedIssue BlockTriage(IReadOnlyList<int> blockers, DateTimeOffset transitionedAt)
    {
        TriageBlockedIssue blocked = TriageBlockedIssue.Untriaged(this, blockers, transitionedAt);
        AddDomainEvent(new Events.IssueTriageBlocked(Id, MonitoredRepositoryId));
        return blocked;
    }
}
