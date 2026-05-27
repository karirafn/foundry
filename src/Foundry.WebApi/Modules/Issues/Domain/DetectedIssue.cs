using Foundry.WebApi.Modules.Monitoring.Domain;

namespace Foundry.WebApi.Modules.Issues.Domain;

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
        string body,
        IssueAuthor author,
        ProviderUrl url,
        IReadOnlyList<string> labels,
        DateTimeOffset detectedAt)
    {
        DetectedIssue issue = new(IssueId.New());
        issue.SetSharedProperties(
            monitoredRepositoryId,
            issueNumber,
            title,
            body,
            author,
            url,
            labels,
            detectedAt);
        return issue;
    }
}
