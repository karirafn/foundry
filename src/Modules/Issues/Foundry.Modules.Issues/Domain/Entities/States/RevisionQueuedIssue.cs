using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using BranchNameValue = Foundry.Shared.BranchName;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class RevisionQueuedIssue : QueuedIssue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => true;

    public override bool WaitsOnBlockers => false;

    public override string? TransitionCause => Trigger.Discriminator;

    public override IssueLifecycleStep LifecycleStep => IssueLifecycleStep.Queued;

    public override RunKind? RunKind => RevisionRunKind.Of(Trigger);

    // Private parameterless constructor for EF Core materialization.
    private RevisionQueuedIssue()
    {
    }

    private RevisionQueuedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public override int TierRank => 0;

    public override BranchNameValue? DispatchBranchName => BranchNameValue.From(BranchName);

    public override DispatchContext Context =>
        new DispatchContext.Revision(BranchName, PullRequestUrl, Trigger) { ContinuedWithoutAnswer = ContinuedWithoutAnswer };

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public override string? LinkedPullRequestUrl => NullIfEmpty(PullRequestUrl);

    // Placeholder replaced by the factory method or EF materialisation; invalid until assigned.
    public RevisionTrigger Trigger { get; private set; } = new RevisionTrigger.ReviewFeedback([], 0, null);

    /// <summary>
    /// The feedback cutoff of the state a revision that consumed no comments started from (issue #925);
    /// <see langword="null"/> when the revision consumed comments or predates the field.
    /// </summary>
    public DateTimeOffset? PriorFeedbackCutoffAt { get; private set; }

    public int RebaseConflictAttempts { get; private set; }

    internal static RevisionQueuedIssue FromReview(
        ReviewIssue source,
        IReadOnlyList<ReviewComment> comments,
        int omittedCommentCount,
        DateTimeOffset? newestCommentAt,
        DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = new RevisionTrigger.ReviewFeedback(comments, omittedCommentCount, newestCommentAt);
        revisionQueued.RecordPersonFeedback(comments, omittedCommentCount, newestCommentAt, transitionedAt);
        revisionQueued.StartRound();
        revisionQueued.ResetChecksFixAttempts();
        revisionQueued.ResetBeforeReadyRounds();
        revisionQueued.RebaseConflictAttempts = source.RebaseConflictAttempts;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromChecksPending(
        ChecksPendingIssue source,
        IReadOnlyList<ReviewComment> comments,
        int omittedCommentCount,
        DateTimeOffset? newestCommentAt,
        DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = new RevisionTrigger.ReviewFeedback(comments, omittedCommentCount, newestCommentAt);
        revisionQueued.RecordPersonFeedback(comments, omittedCommentCount, newestCommentAt, transitionedAt);
        revisionQueued.StartRound();
        revisionQueued.ResetChecksFixAttempts();
        revisionQueued.ResetBeforeReadyRounds();
        revisionQueued.RebaseConflictAttempts = source.RebaseConflictAttempts;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromMergePending(
        MergePendingIssue source,
        IReadOnlyList<ReviewComment> comments,
        int omittedCommentCount,
        DateTimeOffset? newestCommentAt,
        DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = new RevisionTrigger.ReviewFeedback(comments, omittedCommentCount, newestCommentAt);
        revisionQueued.RecordPersonFeedback(comments, omittedCommentCount, newestCommentAt, transitionedAt);
        revisionQueued.StartRound();
        revisionQueued.ResetChecksFixAttempts();
        revisionQueued.ResetBeforeReadyRounds();
        revisionQueued.RebaseConflictAttempts = source.RebaseConflictAttempts;
        return revisionQueued;
    }

    /// <summary>
    /// The reviewer requested changes (ADR 0113): the revision spends one of the round's review fix runs and leaves
    /// <see cref="Issue.HasPersonFeedback"/> as it was, so auto-merge survives it. Its pushed fix gets the full checks
    /// fix budget, as new code does, and the next review pass reads comments after the reviewed state's cutoff.
    /// </summary>
    internal static RevisionQueuedIssue FromReviewInProgress(
        ReviewInProgressIssue source,
        RevisionTrigger.ReviewFindings findings,
        DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = findings;
        revisionQueued.RecordReviewFix();
        revisionQueued.EscalateDemand();
        revisionQueued.StartRound();
        revisionQueued.ResetChecksFixAttempts();
        revisionQueued.ResetBeforeReadyRounds();
        revisionQueued.PriorFeedbackCutoffAt = source.FeedbackCutoffAt;
        revisionQueued.RebaseConflictAttempts = source.RebaseConflictAttempts;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromChecksPendingFix(
        ChecksPendingIssue source,
        IReadOnlyList<FailingCheck> failingChecks,
        string? headSha,
        DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = new RevisionTrigger.FailedChecks(failingChecks, headSha);
        revisionQueued.RecordChecksFix();
        revisionQueued.ResetBeforeReadyRounds();
        revisionQueued.EscalateDemand();
        revisionQueued.StartRound();
        revisionQueued.PriorFeedbackCutoffAt = source.FeedbackCutoffAt;
        revisionQueued.RebaseConflictAttempts = source.RebaseConflictAttempts;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromRebaseConflict(RebasePendingIssue source, DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = new RevisionTrigger.RebaseConflict();
        revisionQueued.StartRound();
        revisionQueued.PriorFeedbackCutoffAt = source.FeedbackCutoffAt;
        revisionQueued.RebaseConflictAttempts = source.RebaseConflictAttempts + 1;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromRevisionFailed(RevisionFailedIssue source, DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = source.Trigger;
        revisionQueued.PriorFeedbackCutoffAt = source.PriorFeedbackCutoffAt;
        revisionQueued.RebaseConflictAttempts = source.RebaseConflictAttempts;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromAwaitingInput(
        AwaitingInputIssue source,
        ResumeTarget.Revision target,
        ClarificationInput input,
        DateTimeOffset answeredAt,
        DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        revisionQueued.AnswerClarification(input, answeredAt);
        revisionQueued.ContinuedWithoutAnswer = input.IsContinuedWithoutAnswer;
        revisionQueued.BranchName = target.BranchName;
        revisionQueued.PullRequestUrl = target.PullRequestUrl;
        revisionQueued.Trigger = new RevisionTrigger.InputProvided();
        revisionQueued.ResetChecksFixAttempts();
        revisionQueued.ResetBeforeReadyRounds();
        revisionQueued.RebaseConflictAttempts = 0;
        return revisionQueued;
    }

    /// <summary>
    /// Triage released an issue that holds open work with a pull request with no open blockers: the work resumes as a
    /// <see cref="RevisionTrigger.SpecChanged"/> revision on its branch and pull request, never a fresh run on a
    /// recomputed branch. The revision consumes no review comments, so it carries no feedback cutoff and the next review
    /// cutoff anchors to its run's start.
    /// </summary>
    internal static RevisionQueuedIssue FromTriage(
        Issue source,
        TriageVerdict.Ready? verdict,
        string branchName,
        string pullRequestUrl,
        DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        if (verdict is not null)
        {
            revisionQueued.RecordTriageVerdict(verdict);
        }

        revisionQueued.ReleaseOpenWork();
        revisionQueued.BranchName = branchName;
        revisionQueued.PullRequestUrl = pullRequestUrl;
        revisionQueued.Trigger = new RevisionTrigger.SpecChanged();
        revisionQueued.StartRound();
        revisionQueued.ResetReviewFixAttempts();
        revisionQueued.ResetChecksFixAttempts();
        revisionQueued.ResetBeforeReadyRounds();
        revisionQueued.RebaseConflictAttempts = 0;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromRevisionBlocked(RevisionBlockedIssue source, DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        revisionQueued.SetBlockedBy(source.BlockedBy);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = source.Trigger;
        revisionQueued.PriorFeedbackCutoffAt = source.PriorFeedbackCutoffAt;
        revisionQueued.RebaseConflictAttempts = source.RebaseConflictAttempts;
        revisionQueued.ContinuedWithoutAnswer = source.ContinuedWithoutAnswer;
        return revisionQueued;
    }

    /// <summary>Open <paramref name="blockers"/> hold the revision until they close: it starts no run meanwhile.</summary>
    public RevisionBlockedIssue Block(IReadOnlyList<int> blockers, DateTimeOffset transitionedAt)
    {
        RevisionBlockedIssue blocked = RevisionBlockedIssue.FromRevisionQueued(this, blockers, transitionedAt);
        AddDomainEvent(new Events.IssueRevisionBlocked(Id, MonitoredRepositoryId));
        return blocked;
    }

    /// <summary>The pull request the queued revision would revise was closed without merging.</summary>
    public ContinuableFailedIssue Fail(string failureReason, FailureCategory failureCategory, DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromRevisionQueued(
            this,
            failureReason,
            failureCategory,
            failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }

    public override RevisionInProgressIssue Claim(WorkerRunId workerRunId, DateTimeOffset claimedAt)
    {
        RevisionInProgressIssue revisionInProgress = RevisionInProgressIssue.FromRevisionQueued(
            this,
            workerRunId,
            claimedAt);
        AddDomainEvent(new Events.IssueRevisionInProgress(Id, MonitoredRepositoryId));
        return revisionInProgress;
    }

    /// <summary>
    /// The issue's spec was rewritten since its last triage (ADR 0125): it goes back to triage holding its branch and
    /// pull request, so a ready verdict resumes the work on them.
    /// </summary>
    public TriageQueuedIssue RetriageRewrittenSpec(DateTimeOffset transitionedAt)
    {
        TriageQueuedIssue queued = TriageQueuedIssue.HoldingOpenWork(
            this,
            OpenWork.Of(BranchName, PullRequestUrl),
            transitionedAt);
        AddDomainEvent(new Events.IssueTriageQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
