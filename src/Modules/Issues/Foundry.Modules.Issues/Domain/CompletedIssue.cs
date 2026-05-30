using Foundry.Modules.Issues.Contracts;

namespace Foundry.Modules.Issues.Domain;

public sealed class CompletedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private CompletedIssue()
    {
    }

    private CompletedIssue(IssueId id) : base(id)
    {
    }

    public string? BranchName { get; private set; }

    public string? PullRequestUrl { get; private set; }

    public DateTimeOffset CompletedAt { get; private set; }

    internal static CompletedIssue FromReview(ReviewIssue source, DateTimeOffset completedAt)
    {
        CompletedIssue completed = new(source.Id);
        completed.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        completed.BranchName = source.BranchName;
        completed.PullRequestUrl = source.PullRequestUrl;
        completed.CompletedAt = completedAt;
        return completed;
    }

    internal static CompletedIssue FromUnchanged(UnchangedIssue source, DateTimeOffset completedAt)
    {
        CompletedIssue completed = new(source.Id);
        completed.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        completed.BranchName = null;
        completed.PullRequestUrl = null;
        completed.CompletedAt = completedAt;
        return completed;
    }
}
