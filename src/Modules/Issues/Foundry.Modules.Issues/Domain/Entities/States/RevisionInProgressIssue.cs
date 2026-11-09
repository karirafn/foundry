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

    public override string? LinkedPullRequestUrl => NullIfEmpty(PullRequestUrl);

    // Placeholder replaced by the factory method or EF materialisation; invalid until assigned.
    public RevisionTrigger Trigger { get; private set; } = new RevisionTrigger.ReviewFeedback([], 0, null);

    public int ChecksFixAttempts { get; private set; }

    public int RebaseConflictAttempts { get; private set; }

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
            source.DetectedAt,
            source.KindClassification);
        revisionInProgress.WorkerRunId = workerRunId;
        revisionInProgress.BranchName = source.BranchName;
        revisionInProgress.PullRequestUrl = source.PullRequestUrl;
        revisionInProgress.Trigger = source.Trigger;
        revisionInProgress.ChecksFixAttempts = source.ChecksFixAttempts;
        revisionInProgress.RebaseConflictAttempts = source.RebaseConflictAttempts;
        return revisionInProgress;
    }

    // MarkInReview and MarkUnchanged perform the same state transition — both return the issue to ReviewIssue
    // carrying the existing BranchName and PullRequestUrl forward. The two-name distinction is intentional:
    // MarkInReview signals that the revision produced (or updated) a PR; MarkUnchanged signals that the worker
    // found nothing to change but the PR still stands. Callers pick the name that matches what the worker
    // reported, which preserves the semantic distinction in logs and domain events without diverging the cutoff
    // logic. Do NOT merge them into one method.

    public ReviewIssue MarkInReview(DateTimeOffset runStartedAt)
    {
        DateTimeOffset feedbackCutoffAt = FeedbackCutoffAt(runStartedAt);
        ReviewIssue review = ReviewIssue.FromRevisionInProgress(this, feedbackCutoffAt);
        AddDomainEvent(new Events.IssueInReview(Id, MonitoredRepositoryId));
        return review;
    }

    public ReviewIssue MarkUnchanged(DateTimeOffset runStartedAt)
    {
        DateTimeOffset feedbackCutoffAt = FeedbackCutoffAt(runStartedAt);
        ReviewIssue review = ReviewIssue.FromRevisionInProgress(this, feedbackCutoffAt);
        AddDomainEvent(new Events.IssueInReview(Id, MonitoredRepositoryId));
        return review;
    }

    // MarkRebasePending and MarkUnchangedRebasePending mirror the MarkInReview/MarkUnchanged split:
    // MarkRebasePending signals the revision produced (or updated) a PR whose branch is now rebased before checks;
    // MarkUnchangedRebasePending signals the worker found nothing to change but the branch may still be behind
    // the default branch, so it is rebased all the same (ADR 0087).
    // The semantic distinction is preserved in logs and events without diverging the cutoff logic.
    // Do NOT merge them into one method.

    public RebasePendingIssue MarkRebasePending(DateTimeOffset runStartedAt)
    {
        DateTimeOffset feedbackCutoffAt = FeedbackCutoffAt(runStartedAt);
        RebasePendingIssue rebasePending = RebasePendingIssue.FromRevisionInProgress(this, feedbackCutoffAt);
        AddDomainEvent(new Events.IssueRebasePending(
            Id,
            MonitoredRepositoryId,
            rebasePending.IssueNumber,
            rebasePending.EnteredRebasePendingAt));
        return rebasePending;
    }

    public RebasePendingIssue MarkUnchangedRebasePending(DateTimeOffset runStartedAt)
    {
        DateTimeOffset feedbackCutoffAt = FeedbackCutoffAt(runStartedAt);
        RebasePendingIssue rebasePending = RebasePendingIssue.FromRevisionInProgress(this, feedbackCutoffAt);
        AddDomainEvent(new Events.IssueRebasePending(
            Id,
            MonitoredRepositoryId,
            rebasePending.IssueNumber,
            rebasePending.EnteredRebasePendingAt));
        return rebasePending;
    }

    // The cutoff the next review pass reads comments after. A ReviewFeedback trigger anchors to the newest comment
    // it consumed, so feedback posted after that batch is still read. Every other trigger (FailedChecks,
    // RebaseConflict) consumed no comments, so it anchors to the run's start time — the same as a first run — and a
    // comment posted while the run was working is not dropped. A ReviewFeedback trigger with no consumed timestamp
    // falls back to the run's start time as well.
    private DateTimeOffset FeedbackCutoffAt(DateTimeOffset runStartedAt) =>
        Trigger is RevisionTrigger.ReviewFeedback { NewestConsumedCommentAt: DateTimeOffset newestConsumedCommentAt }
            ? newestConsumedCommentAt
            : runStartedAt;

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
