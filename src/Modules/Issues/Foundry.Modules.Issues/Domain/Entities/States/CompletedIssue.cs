using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class CompletedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private CompletedIssue()
    {
    }

    private CompletedIssue(IssueId id) : base(id)
    {
    }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

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

    internal static CompletedIssue FromInProgress(
        InProgressIssue source,
        string branchName,
        string pullRequestUrl,
        DateTimeOffset completedAt)
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
        completed.BranchName = branchName;
        completed.PullRequestUrl = pullRequestUrl;
        completed.CompletedAt = completedAt;
        return completed;
    }
}
