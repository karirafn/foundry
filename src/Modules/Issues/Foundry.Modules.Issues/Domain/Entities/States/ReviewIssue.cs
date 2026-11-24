using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class ReviewIssue : Issue
{
    public override IssueStage Stage => IssueStage.Merge;

    public override bool NeedsAction => true;

    public override bool AwaitsDispatch => false;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    // The newest outcome explains why the issue reached review: a review runs after the merge, the merge after the checks.
    public override string? TransitionCause => ReviewOutcome?.Discriminator ?? MergeOutcome?.Discriminator ?? ChecksOutcome.Discriminator;

    public override IssueLifecycleStep LifecycleStep => IssueLifecycleStep.YourReview;

    public override RunKind? RunKind => null;

    // Private parameterless constructor for EF Core materialization.
    private ReviewIssue()
    {
    }

    private ReviewIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public override string? LinkedPullRequestUrl => NullIfEmpty(PullRequestUrl);

    public DateTimeOffset FeedbackCutoffAt { get; private set; }

    public int RebaseConflictAttempts { get; private set; }

    public ChecksOutcome ChecksOutcome { get; private set; } = new ChecksOutcome.Passed();

    /// <summary>
    /// How the post-run rebase resolved, carried from checks-pending (ADR 0087). <see langword="null"/> when the
    /// issue entered checks-pending before the rebase step existed.
    /// </summary>
    public RebaseOutcome? RebaseOutcome { get; private set; }

    /// <summary>
    /// The Verified Head: the pull request's head commit as read by the status read in the poll that resolved
    /// the checks verdict. <see langword="null"/> for rows that predate the field or verdicts recorded before it
    /// was carried; the review pass treats null as moved, so such an issue is re-checked once.
    /// </summary>
    public string? VerifiedHeadSha { get; private set; }

    /// <summary>
    /// Why Foundry did not merge the pull request itself (ADR 0105): <see cref="ValueObjects.MergeOutcome.Disabled"/>
    /// or <see cref="ValueObjects.MergeOutcome.Skipped"/> when the issue came from merge-pending, and
    /// <see langword="null"/> when it never entered merge-pending.
    /// </summary>
    public MergeOutcome? MergeOutcome { get; private set; }

    /// <summary>
    /// Why the review step did not judge the pull request (ADR 0113): <see cref="ValueObjects.ReviewOutcome.Skipped"/>
    /// when the review run failed with no automatic retry left, and <see langword="null"/> otherwise.
    /// </summary>
    public ReviewOutcome? ReviewOutcome { get; private set; }

    /// <summary>
    /// The repository's Merge When Checks Pass as the review step carried it, kept so Retry review queues the review
    /// with the flag the skipped review had. False when the issue did not come from the review step.
    /// </summary>
    public bool AutoMerge { get; private set; }

    internal static ReviewIssue FromChecksPending(
        ChecksPendingIssue source,
        ChecksOutcome checksOutcome,
        string? verifiedHeadSha,
        DateTimeOffset transitionedAt)
    {
        ReviewIssue review = new(source.Id, transitionedAt);
        review.CopySharedPropertiesFrom(source);
        review.WorkerRunId = source.WorkerRunId;
        review.BranchName = source.BranchName;
        review.PullRequestUrl = source.PullRequestUrl;
        review.FeedbackCutoffAt = source.FeedbackCutoffAt;
        review.ChecksOutcome = checksOutcome;
        review.RebaseOutcome = source.RebaseOutcome;
        review.RebaseConflictAttempts = source.RebaseConflictAttempts;
        review.VerifiedHeadSha = verifiedHeadSha;
        return review;
    }

    /// <summary>
    /// A catch-up rebase could not bring the branch up to date (ADR 0111). The checks had passed on the head the
    /// refused merge was pinned to, so the issue keeps that verdict and head, the rebase's own outcome, and records
    /// the merge as <c>skipped(catch_up_failed)</c>.
    /// </summary>
    internal static ReviewIssue FromFailedCatchUp(
        RebasePendingIssue source,
        RebaseOutcome rebaseOutcome,
        DateTimeOffset transitionedAt)
    {
        ReviewIssue review = new(source.Id, transitionedAt);
        review.CopySharedPropertiesFrom(source);
        review.WorkerRunId = source.WorkerRunId;
        review.BranchName = source.BranchName;
        review.PullRequestUrl = source.PullRequestUrl;
        review.FeedbackCutoffAt = source.FeedbackCutoffAt;
        review.ChecksOutcome = new ChecksOutcome.Passed();
        review.RebaseOutcome = rebaseOutcome;
        review.RebaseConflictAttempts = source.RebaseConflictAttempts;
        review.VerifiedHeadSha = source.CatchUpHeadSha;
        review.MergeOutcome = new MergeOutcome.Skipped(MergeSkipReasons.CatchUpFailed);
        return review;
    }

    /// <summary>
    /// The review step handed the pull request to a person: the reviewer approved it for a person, requested changes,
    /// approved it without auto-merge, or its run failed open with <paramref name="reviewOutcome"/> recorded. The
    /// reviewed commit is the Verified Head, so a person's push since sends the issue back through the checks gate.
    /// </summary>
    internal static ReviewIssue FromReviewInProgress(
        ReviewInProgressIssue source,
        ReviewOutcome? reviewOutcome,
        DateTimeOffset transitionedAt)
    {
        ReviewIssue review = new(source.Id, transitionedAt);
        review.CopySharedPropertiesFrom(source);
        review.WorkerRunId = source.WorkerRunId;
        review.BranchName = source.BranchName;
        review.PullRequestUrl = source.PullRequestUrl;
        review.FeedbackCutoffAt = source.FeedbackCutoffAt;
        review.ChecksOutcome = source.ChecksOutcome;
        review.RebaseOutcome = source.RebaseOutcome;
        review.RebaseConflictAttempts = source.RebaseConflictAttempts;
        review.VerifiedHeadSha = source.ReviewedCommit;
        review.ReviewOutcome = reviewOutcome;
        review.AutoMerge = source.AutoMerge;
        return review;
    }

    internal static ReviewIssue FromMergePending(
        MergePendingIssue source,
        MergeOutcome mergeOutcome,
        DateTimeOffset transitionedAt)
    {
        ReviewIssue review = new(source.Id, transitionedAt);
        review.CopySharedPropertiesFrom(source);
        review.WorkerRunId = source.WorkerRunId;
        review.BranchName = source.BranchName;
        review.PullRequestUrl = source.PullRequestUrl;
        review.FeedbackCutoffAt = source.FeedbackCutoffAt;
        review.ChecksOutcome = source.ChecksOutcome;
        review.RebaseOutcome = source.RebaseOutcome;
        review.RebaseConflictAttempts = source.RebaseConflictAttempts;
        review.VerifiedHeadSha = source.HeadSha;
        review.MergeOutcome = mergeOutcome;
        return review;
    }

    /// <summary>
    /// New commits reached the pull request after its checks verdict: the issue goes back to the checks gate,
    /// which reads the verdict again against the new head.
    /// </summary>
    public ChecksPendingIssue RecheckAfterHeadMoved(DateTimeOffset transitionedAt)
    {
        ChecksPendingIssue checksPending = ChecksPendingIssue.FromReview(this, transitionedAt);
        AddDomainEvent(new Events.IssueChecksPending(
            Id,
            MonitoredRepositoryId,
            checksPending.IssueNumber,
            checksPending.EnteredChecksPendingAt));
        return checksPending;
    }

    /// <summary>
    /// The pull request's checks read green on the same head after a rerun: the issue goes back to the checks gate,
    /// which confirms and routes the green verdict (ADR 0108). Returns <see langword="null"/> when the stored verdict
    /// is already <see cref="ChecksOutcome.Passed"/>, which is never re-read, so the cycle ends.
    /// </summary>
    public ChecksPendingIssue? RecheckAfterChecksPassed(DateTimeOffset transitionedAt)
    {
        if (ChecksOutcome is ChecksOutcome.Passed)
        {
            return null;
        }

        ChecksPendingIssue checksPending = ChecksPendingIssue.FromReviewAfterChecksRerun(this, transitionedAt);
        AddDomainEvent(new Events.IssueChecksPending(
            Id,
            MonitoredRepositoryId,
            checksPending.IssueNumber,
            checksPending.EnteredChecksPendingAt));
        return checksPending;
    }

    /// <summary>
    /// A person retries a review that failed open (ADR 0113): the issue waits for a review worker again, for the same
    /// reviewed commit. Returns <see langword="null"/> unless the review was skipped, the only case with a review to retry.
    /// </summary>
    public ReviewQueuedIssue? RetryReview(DateTimeOffset transitionedAt)
    {
        if (ReviewOutcome is not ValueObjects.ReviewOutcome.Skipped || VerifiedHeadSha is null)
        {
            return null;
        }

        ReviewQueuedIssue queued = ReviewQueuedIssue.FromSkippedReview(this, VerifiedHeadSha, transitionedAt);
        AddDomainEvent(new Events.IssueReviewQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    public RevisionQueuedIssue Revise(
        IReadOnlyList<ReviewComment> comments,
        DateTimeOffset transitionedAt,
        int omittedCommentCount = 0,
        DateTimeOffset? newestCommentAt = null)
    {
        RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromReview(
            this,
            comments,
            omittedCommentCount,
            newestCommentAt,
            transitionedAt);
        AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
        return revisionQueued;
    }

    public CompletedIssue Complete(DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromReview(this, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }

    public ContinuableFailedIssue Fail(string failureReason, FailureCategory failureCategory, DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromReview(
            this,
            failureReason,
            failureCategory,
            failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
