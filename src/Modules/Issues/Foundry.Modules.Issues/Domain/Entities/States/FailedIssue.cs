using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class FailedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private FailedIssue()
    {
    }

    private FailedIssue(IssueId id) : base(id)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public string FailureReason { get; private set; } = string.Empty;

    public string FailureCategory { get; private set; } = string.Empty;

    public DateTimeOffset FailedAt { get; private set; }

    internal static FailedIssue FromInProgress(
        InProgressIssue source,
        string failureReason,
        string failureCategory,
        DateTimeOffset failedAt)
    {
        FailedIssue failed = new(source.Id);
        failed.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        failed.WorkerRunId = source.WorkerRunId;
        failed.FailureReason = failureReason;
        failed.FailureCategory = failureCategory;
        failed.FailedAt = failedAt;
        return failed;
    }

    internal static FailedIssue FromReview(
        ReviewIssue source,
        string failureReason,
        string failureCategory,
        DateTimeOffset failedAt)
    {
        FailedIssue failed = new(source.Id);
        failed.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        failed.WorkerRunId = source.WorkerRunId;
        failed.FailureReason = failureReason;
        failed.FailureCategory = failureCategory;
        failed.FailedAt = failedAt;
        return failed;
    }

    public FreshQueuedIssue Retry()
    {
        FreshQueuedIssue queued = FreshQueuedIssue.FromRetry(this);
        AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
