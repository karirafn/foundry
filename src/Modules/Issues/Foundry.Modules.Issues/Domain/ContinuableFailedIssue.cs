using Foundry.Modules.Issues.Contracts;

namespace Foundry.Modules.Issues.Domain;

public sealed class ContinuableFailedIssue : Issue
{
    private const string DefaultProgressFromReview = "PR was opened and reviewed";

    // Private parameterless constructor for EF Core materialization.
    private ContinuableFailedIssue()
    {
    }

    private ContinuableFailedIssue(IssueId id) : base(id)
    {
    }

    public Guid WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string? PullRequestUrl { get; private set; }

    public string LatestProgress { get; private set; } = string.Empty;

    public string FailureReason { get; private set; } = string.Empty;

    public DateTimeOffset FailedAt { get; private set; }

    internal static ContinuableFailedIssue FromInProgress(
        InProgressIssue source,
        Guid workerRunId,
        string branchName,
        string latestProgress,
        string failureReason,
        DateTimeOffset failedAt)
    {
        ContinuableFailedIssue continuable = new(source.Id);
        continuable.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        continuable.WorkerRunId = workerRunId;
        continuable.BranchName = branchName;
        continuable.LatestProgress = latestProgress;
        continuable.FailureReason = failureReason;
        continuable.FailedAt = failedAt;
        return continuable;
    }

    internal static ContinuableFailedIssue FromReview(
        ReviewIssue source,
        string failureReason,
        DateTimeOffset failedAt)
    {
        ContinuableFailedIssue continuable = new(source.Id);
        continuable.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        continuable.WorkerRunId = source.WorkerRunId;
        continuable.BranchName = source.BranchName;
        continuable.PullRequestUrl = source.PullRequestUrl;
        continuable.LatestProgress = DefaultProgressFromReview;
        continuable.FailureReason = failureReason;
        continuable.FailedAt = failedAt;
        return continuable;
    }
}
