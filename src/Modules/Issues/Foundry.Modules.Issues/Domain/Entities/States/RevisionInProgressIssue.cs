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

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private RevisionInProgressIssue()
    {
    }

    private RevisionInProgressIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
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

    internal static RevisionInProgressIssue FromRevisionQueued(
        RevisionQueuedIssue source,
        WorkerRunId workerRunId,
        DateTimeOffset claimedAt)
    {
        RevisionInProgressIssue revisionInProgress = new(source.Id, claimedAt);
        revisionInProgress.CopySharedPropertiesFrom(source);
        revisionInProgress.WorkerRunId = workerRunId;
        revisionInProgress.BranchName = source.BranchName;
        revisionInProgress.PullRequestUrl = source.PullRequestUrl;
        revisionInProgress.Trigger = source.Trigger;
        revisionInProgress.ChecksFixAttempts = source.ChecksFixAttempts;
        revisionInProgress.RebaseConflictAttempts = source.RebaseConflictAttempts;
        return revisionInProgress;
    }

    internal static RevisionInProgressIssue FromPublishFailed(
        PublishFailedIssue source,
        RevisionTrigger trigger,
        DateTimeOffset transitionedAt)
    {
        RevisionInProgressIssue revisionInProgress = new(source.Id, transitionedAt);
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
    /// The revision's work could not go to its branch, so it went to <paramref name="sideBranchName"/> for
    /// <paramref name="divergenceReason"/> (ADR 0100, ADR 0101).
    /// </summary>
    public BranchDivergedIssue MarkBranchDiverged(
        string sideBranchName,
        BranchDivergenceReason divergenceReason,
        DateTimeOffset transitionedAt)
    {
        BranchDivergedIssue diverged = BranchDivergedIssue.FromRevisionInProgress(
            this,
            sideBranchName,
            divergenceReason,
            transitionedAt);
        AddDomainEvent(new Events.IssueBranchDiverged(Id, MonitoredRepositoryId));
        return diverged;
    }

    // MarkRebasePending and MarkUnchangedRebasePending perform the same transition under two names:
    // MarkRebasePending signals the revision produced (or updated) a PR whose branch is now rebased before checks;
    // MarkUnchangedRebasePending signals the worker found nothing to change but the branch may still be behind
    // the default branch, so it is rebased all the same (ADR 0087).
    // The semantic distinction is preserved in logs and events without diverging the cutoff logic.
    // Do NOT merge them into one method.

    public RebasePendingIssue MarkRebasePending(DateTimeOffset runStartedAt, DateTimeOffset transitionedAt)
    {
        DateTimeOffset feedbackCutoffAt = FeedbackCutoffAt(runStartedAt);
        RebasePendingIssue rebasePending = RebasePendingIssue.FromRevisionInProgress(this, feedbackCutoffAt, transitionedAt);
        AddDomainEvent(new Events.IssueRebasePending(
            Id,
            MonitoredRepositoryId,
            rebasePending.IssueNumber,
            rebasePending.EnteredRebasePendingAt));
        return rebasePending;
    }

    public RebasePendingIssue MarkUnchangedRebasePending(DateTimeOffset runStartedAt, DateTimeOffset transitionedAt)
    {
        DateTimeOffset feedbackCutoffAt = FeedbackCutoffAt(runStartedAt);
        RebasePendingIssue rebasePending = RebasePendingIssue.FromRevisionInProgress(this, feedbackCutoffAt, transitionedAt);
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
    public AwaitingInputIssue MarkAwaitingInput(
        IReadOnlyList<InputQuestion> questions,
        DateTimeOffset askedAt,
        DateTimeOffset transitionedAt)
    {
        AwaitingInputIssue awaiting = AwaitingInputIssue.FromRevisionInProgress(this, questions, askedAt, transitionedAt);
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
