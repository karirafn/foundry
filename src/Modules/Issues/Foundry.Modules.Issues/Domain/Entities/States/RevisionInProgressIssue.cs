using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class RevisionInProgressIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private RevisionInProgressIssue()
    {
    }

    private RevisionInProgressIssue(IssueId id) : base(id)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    // Placeholder replaced by the factory method or EF materialisation; invalid until assigned.
    public RevisionTrigger Trigger { get; private set; } = new RevisionTrigger.ReviewFeedback([], 0, null);

    public int ChecksFixAttempts { get; private set; }

    internal static RevisionInProgressIssue FromRevisionQueued(RevisionQueuedIssue source, WorkerRunId workerRunId)
    {
        RevisionInProgressIssue revisionInProgress = new(source.Id);
        revisionInProgress.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        revisionInProgress.WorkerRunId = workerRunId;
        revisionInProgress.BranchName = source.BranchName;
        revisionInProgress.PullRequestUrl = source.PullRequestUrl;
        revisionInProgress.Trigger = source.Trigger;
        revisionInProgress.ChecksFixAttempts = source.ChecksFixAttempts;
        return revisionInProgress;
    }

    // MarkInReview and MarkUnchanged perform the same state transition — both return the issue to ReviewIssue
    // carrying the existing BranchName and PullRequestUrl forward. The two-name distinction is intentional:
    // MarkInReview signals that the revision produced (or updated) a PR; MarkUnchanged signals that the worker
    // found nothing to change but the PR still stands. Callers pick the name that matches what the worker
    // reported, which preserves the semantic distinction in logs and domain events without diverging the cutoff
    // logic. Do NOT merge them into one method.

    public ReviewIssue MarkInReview()
    {
        DateTimeOffset feedbackCutoffAt = Trigger is RevisionTrigger.ReviewFeedback rf
            ? rf.NewestConsumedCommentAt ?? DateTimeOffset.UtcNow
            : DateTimeOffset.UtcNow;
        ReviewIssue review = ReviewIssue.FromRevisionInProgress(this, feedbackCutoffAt);
        AddDomainEvent(new Events.IssueInReview(Id, MonitoredRepositoryId));
        return review;
    }

    public ReviewIssue MarkUnchanged()
    {
        DateTimeOffset feedbackCutoffAt = Trigger is RevisionTrigger.ReviewFeedback rf
            ? rf.NewestConsumedCommentAt ?? DateTimeOffset.UtcNow
            : DateTimeOffset.UtcNow;
        ReviewIssue review = ReviewIssue.FromRevisionInProgress(this, feedbackCutoffAt);
        AddDomainEvent(new Events.IssueInReview(Id, MonitoredRepositoryId));
        return review;
    }

    // MarkChecksPending and MarkUnchangedChecksPending mirror the MarkInReview/MarkUnchanged split:
    // MarkChecksPending signals the revision produced (or updated) a PR that is now awaiting checks;
    // MarkUnchangedChecksPending signals the worker found nothing to change but checks still need to run.
    // The semantic distinction is preserved in logs and events without diverging the cutoff logic.
    // Do NOT merge them into one method.

    public ChecksPendingIssue MarkChecksPending()
    {
        DateTimeOffset feedbackCutoffAt = Trigger is RevisionTrigger.ReviewFeedback rf
            ? rf.NewestConsumedCommentAt ?? DateTimeOffset.UtcNow
            : DateTimeOffset.UtcNow;
        ChecksPendingIssue checksPending = ChecksPendingIssue.FromRevisionInProgress(this, feedbackCutoffAt);
        AddDomainEvent(new Events.IssueChecksPending(Id, MonitoredRepositoryId));
        return checksPending;
    }

    public ChecksPendingIssue MarkUnchangedChecksPending()
    {
        DateTimeOffset feedbackCutoffAt = Trigger is RevisionTrigger.ReviewFeedback rf
            ? rf.NewestConsumedCommentAt ?? DateTimeOffset.UtcNow
            : DateTimeOffset.UtcNow;
        ChecksPendingIssue checksPending = ChecksPendingIssue.FromRevisionInProgress(this, feedbackCutoffAt);
        AddDomainEvent(new Events.IssueChecksPending(Id, MonitoredRepositoryId));
        return checksPending;
    }

    public RevisionFailedIssue MarkFailed(
        string failureReason,
        FailureCategory failureCategory,
        DateTimeOffset failedAt)
    {
        RevisionFailedIssue failed = RevisionFailedIssue.FromRevisionInProgress(
            this,
            failureReason,
            failureCategory,
            failedAt);
        AddDomainEvent(new Events.IssueRevisionFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
