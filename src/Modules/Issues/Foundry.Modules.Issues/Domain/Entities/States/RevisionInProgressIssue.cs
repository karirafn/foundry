using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class RevisionInProgressIssue : Issue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => false;

    public override IssueCodeStep? CodeStep => null;

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
        revisionInProgress.CopySharedPropertiesFrom(source);
        revisionInProgress.WorkerRunId = workerRunId;
        revisionInProgress.BranchName = source.BranchName;
        revisionInProgress.PullRequestUrl = source.PullRequestUrl;
        revisionInProgress.Trigger = source.Trigger;
        revisionInProgress.ChecksFixAttempts = source.ChecksFixAttempts;
        revisionInProgress.RebaseConflictAttempts = source.RebaseConflictAttempts;
        return revisionInProgress;
    }

    internal static RevisionInProgressIssue FromPublishFailed(PublishFailedIssue source, RevisionTrigger trigger)
    {
        RevisionInProgressIssue revisionInProgress = new(source.Id);
        revisionInProgress.CopySharedPropertiesFrom(source);
        revisionInProgress.WorkerRunId = source.WorkerRunId;
        revisionInProgress.BranchName = source.BranchName;
        revisionInProgress.PullRequestUrl = source.PullRequestUrl;
        revisionInProgress.Trigger = trigger;
        revisionInProgress.ChecksFixAttempts = source.ChecksFixAttempts;
        revisionInProgress.RebaseConflictAttempts = source.RebaseConflictAttempts;
        return revisionInProgress;
    }

    /// <summary>
    /// The revision's publish retries ran out (ADR 0100); its unpublished work waits for Retry publish.
    /// </summary>
    public PublishFailedIssue MarkPublishFailed(string failureReason, DateTimeOffset failedAt)
    {
        PublishFailedIssue publishFailed = PublishFailedIssue.FromRevisionInProgress(this, failureReason, failedAt);
        AddDomainEvent(new Events.IssuePublishFailed(Id, MonitoredRepositoryId));
        return publishFailed;
    }

    /// <summary>
    /// The Diff gate found possible secrets in the revision's commits, so nothing was pushed (#788); the unpublished
    /// work waits for an operator to discard it.
    /// </summary>
    public PublishBlockedIssue MarkPublishBlocked(DateTimeOffset blockedAt)
    {
        PublishBlockedIssue blocked = PublishBlockedIssue.FromRevisionInProgress(this, blockedAt);
        AddDomainEvent(new Events.IssuePublishBlocked(Id, MonitoredRepositoryId));
        return blocked;
    }

    /// <summary>
    /// The revision's work could not be merged into the branch someone pushed to during the run, so it went to
    /// <paramref name="sideBranchName"/> (ADR 0100).
    /// </summary>
    public BranchDivergedIssue MarkBranchDiverged(string sideBranchName)
    {
        BranchDivergedIssue diverged = BranchDivergedIssue.FromRevisionInProgress(this, sideBranchName);
        AddDomainEvent(new Events.IssueBranchDiverged(Id, MonitoredRepositoryId));
        return diverged;
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

    /// <summary>The revision run stopped to ask a person (ADR 0096): its pull request is open, so input resumes a revision.</summary>
    public AwaitingInputIssue MarkAwaitingInput(IReadOnlyList<InputQuestion> questions, DateTimeOffset askedAt)
    {
        AwaitingInputIssue awaiting = AwaitingInputIssue.FromRevisionInProgress(this, questions, askedAt);
        AddDomainEvent(new Events.IssueAwaitingInput(Id, MonitoredRepositoryId, IssueNumber));
        return awaiting;
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
