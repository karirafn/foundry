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

    internal static IneligibleIssue FromDetected(DetectedIssue detected, IReadOnlyList<EligibilityViolation> violations)
    {
        if (violations.Count == 0)
        {
            throw new ArgumentException(
                "An ineligible issue must have at least one eligibility violation.",
                nameof(violations));
        }

        IneligibleIssue ineligible = new(detected.Id);
        ineligible.SetSharedProperties(
            detected.MonitoredRepositoryId,
            detected.IssueNumber,
            detected.Title,
            detected.Body,
            detected.Author,
            detected.Url,
            detected.Labels,
            detected.DetectedAt);
        ineligible.Violations = violations;
        return ineligible;
    }
}
