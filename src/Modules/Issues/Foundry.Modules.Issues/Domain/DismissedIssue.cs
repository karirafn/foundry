using Foundry.Modules.Issues.Contracts;

namespace Foundry.Modules.Issues.Domain;

public sealed class DismissedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private DismissedIssue()
    {
    }

    private DismissedIssue(IssueId id) : base(id)
    {
    }

    public DateTimeOffset CompletedAt { get; private set; }

    internal static DismissedIssue FromUnchanged(UnchangedIssue source, DateTimeOffset completedAt)
    {
        DismissedIssue dismissed = new(source.Id);
        dismissed.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        dismissed.CompletedAt = completedAt;
        return dismissed;
    }
}
