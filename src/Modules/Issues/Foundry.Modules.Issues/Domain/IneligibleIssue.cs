using Foundry.Modules.Issues.Contracts;

namespace Foundry.Modules.Issues.Domain;

public sealed class IneligibleIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private IneligibleIssue()
    {
    }

    private IneligibleIssue(IssueId id) : base(id)
    {
    }

    public IReadOnlyList<EligibilityViolation> Violations { get; private set; } = [];

    public Issue ClearViolations()
    {
        if (BlockedBy.Count > 0)
        {
            BlockedIssue blocked = BlockedIssue.FromIneligible(this);
            AddDomainEvent(new Events.IssueBlocked(Id, MonitoredRepositoryId));
            return blocked;
        }

        QueuedIssue queued = QueuedIssue.FromIneligible(this);
        AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    public void UpdateViolations(IReadOnlyList<EligibilityViolation> violations)
    {
        if (violations.Count == 0)
        {
            throw new ArgumentException(
                "An ineligible issue must have at least one eligibility violation.",
                nameof(violations));
        }

        Violations = violations;
    }

    internal static IneligibleIssue FromDetected(DetectedIssue detected, IReadOnlyList<EligibilityViolation> violations)
    {
        return FromIssue(detected.Id, detected, violations);
    }

    internal static IneligibleIssue FromQueued(QueuedIssue queued, IReadOnlyList<EligibilityViolation> violations)
    {
        return FromIssue(queued.Id, queued, violations);
    }

    private static IneligibleIssue FromIssue(IssueId id, Issue source, IReadOnlyList<EligibilityViolation> violations)
    {
        if (violations.Count == 0)
        {
            throw new ArgumentException(
                "An ineligible issue must have at least one eligibility violation.",
                nameof(violations));
        }

        IneligibleIssue ineligible = new(id);
        ineligible.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        ineligible.Violations = violations;
        return ineligible;
    }
}
