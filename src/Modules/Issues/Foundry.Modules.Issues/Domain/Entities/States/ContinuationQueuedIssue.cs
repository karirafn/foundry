using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class ContinuationQueuedIssue : Issue
{
    public const int FailureReasonMaxLength = 500;
    public const int TierRank = 1;

    // Private parameterless constructor for EF Core materialization.
    private ContinuationQueuedIssue()
    {
    }

    private ContinuationQueuedIssue(IssueId id) : base(id)
    {
    }

    public string BranchName { get; private set; } = string.Empty;

    public string FailureReason { get; private set; } = string.Empty;

    internal static ContinuationQueuedIssue FromContinuableFailed(ContinuableFailedIssue source)
    {
        ContinuationQueuedIssue queued = new(source.Id);
        queued.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        queued.BranchName = source.BranchName;
        queued.FailureReason = source.FailureReason.Length > FailureReasonMaxLength
            ? source.FailureReason[..FailureReasonMaxLength]
            : source.FailureReason;
        return queued;
    }

    internal static ContinuationQueuedIssue FromReview(ReviewIssue source)
    {
        ContinuationQueuedIssue queued = new(source.Id);
        queued.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        queued.BranchName = source.BranchName;
        queued.FailureReason = string.Empty;
        return queued;
    }

    public InProgressIssue Claim(Guid workerRunId)
    {
        InProgressIssue inProgress = InProgressIssue.FromContinuationQueued(this, workerRunId);
        AddDomainEvent(new Events.IssueInProgress(Id, MonitoredRepositoryId));
        return inProgress;
    }
}
