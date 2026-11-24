using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using FailureCategoryVO = Foundry.Modules.Workers.Contracts.FailureCategory;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class ChecksPendingIssue : Issue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => false;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => IssueCodeStep.Checks;

    public override string? TransitionCause => Reason.Discriminator;

    public override IssueLifecycleStep LifecycleStep => IssueLifecycleStep.Checks;

    public override RunKind? RunKind => null;

    /// <summary>
    /// Maximum number of FailedChecks revision containers Foundry will dispatch across the lifetime
    /// of an issue before transitioning it back to review instead of retrying.
    /// </summary>
    public const int ChecksMaxFixPushes = 2;

    // Private parameterless constructor for EF Core materialization.
    private ChecksPendingIssue()
    {
    }

    private ChecksPendingIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public override string? LinkedPullRequestUrl => NullIfEmpty(PullRequestUrl);

    public DateTimeOffset EnteredChecksPendingAt { get; private set; }

    public DateTimeOffset FeedbackCutoffAt { get; private set; }

    public int RebaseConflictAttempts { get; private set; }

    /// <summary>
    /// How the post-run rebase resolved before the issue entered the checks gate (ADR 0087).
    /// <see langword="null"/> for issues that entered checks-pending before the rebase step existed.
    /// </summary>
    public RebaseOutcome? RebaseOutcome { get; private set; }

    /// <summary>
    /// Why the issue is waiting on checks. Shown in the issue pane; the gate behaves the same for every reason.
    /// </summary>
    public ChecksPendingReason Reason { get; private set; } = new ChecksPendingReason.AfterRun();

    internal static ChecksPendingIssue FromRebasePending(
        RebasePendingIssue source,
        RebaseOutcome rebaseOutcome,
        DateTimeOffset transitionedAt)
    {
        ChecksPendingIssue checksPending = new(source.Id, transitionedAt);
        checksPending.CopySharedPropertiesFrom(source);
        checksPending.WorkerRunId = source.WorkerRunId;
        checksPending.BranchName = source.BranchName;
        checksPending.PullRequestUrl = source.PullRequestUrl;
        checksPending.EnteredChecksPendingAt = DateTimeOffset.UtcNow;
        checksPending.FeedbackCutoffAt = source.FeedbackCutoffAt;
        checksPending.RebaseConflictAttempts = rebaseOutcome is RebaseOutcome.UpToDate or RebaseOutcome.Rebased
            ? 0
            : source.RebaseConflictAttempts;
        checksPending.RebaseOutcome = rebaseOutcome;
        checksPending.Reason = source.Reason is RebasePendingReason.CatchUp
            ? new ChecksPendingReason.CatchUp()
            : new ChecksPendingReason.AfterRun();
        return checksPending;
    }

    /// <summary>
    /// New commits reached a review issue's pull request: the checks gate starts over with a fresh fix budget,
    /// because every new budget follows a person's push, the same rule review feedback follows. The push starts a
    /// person's round, so the reviewer's fix budget starts over too (ADR 0106, ADR 0113).
    /// </summary>
    internal static ChecksPendingIssue FromReview(ReviewIssue source, DateTimeOffset transitionedAt)
    {
        ChecksPendingIssue checksPending = FromReviewKeepingReviewFixes(source, transitionedAt);
        checksPending.ResetReviewFixAttempts();
        return checksPending;
    }

    /// <summary>
    /// A review issue's checks read green on the same commits after a rerun: the gate confirms the green verdict
    /// and routes it. Nobody pushed, so the fix budget starts exhausted: a red second read (a flaky job between the
    /// two reads) goes to review instead of spending fix runs on unchanged code (ADR 0108). No person's round
    /// started either, so the reviewer's fix budget carries on.
    /// </summary>
    internal static ChecksPendingIssue FromReviewAfterChecksRerun(ReviewIssue source, DateTimeOffset transitionedAt)
    {
        ChecksPendingIssue checksPending = FromReviewKeepingReviewFixes(source, transitionedAt);
        checksPending.ExhaustChecksFixAttempts();
        checksPending.Reason = new ChecksPendingReason.ChecksRerun();
        return checksPending;
    }

    private static ChecksPendingIssue FromReviewKeepingReviewFixes(ReviewIssue source, DateTimeOffset transitionedAt)
    {
        ChecksPendingIssue checksPending = new(source.Id, transitionedAt);
        checksPending.CopySharedPropertiesFrom(source);
        checksPending.WorkerRunId = source.WorkerRunId;
        checksPending.BranchName = source.BranchName;
        checksPending.PullRequestUrl = source.PullRequestUrl;
        checksPending.EnteredChecksPendingAt = transitionedAt;
        checksPending.FeedbackCutoffAt = source.FeedbackCutoffAt;
        checksPending.ResetChecksFixAttempts();
        checksPending.RebaseConflictAttempts = source.RebaseConflictAttempts;
        checksPending.RebaseOutcome = source.RebaseOutcome;
        checksPending.Reason = new ChecksPendingReason.HeadMoved();
        return checksPending;
    }

    /// <summary>
    /// Routes the checks verdict to the next state:
    /// <list type="bullet">
    /// <item><b>Review step:</b> <see cref="ChecksOutcome.Passed"/> or <see cref="ChecksOutcome.NoneDetected"/> with
    /// <paramref name="reviewPullRequests"/> on and a known <paramref name="headSha"/> → <see cref="ReviewQueuedIssue"/>,
    /// reviewing that head (ADR 0113). The review's verdict then decides between merge and review.</item>
    /// <item><b>Merge:</b> <see cref="ChecksOutcome.Passed"/> with <paramref name="autoMerge"/> on and a known
    /// <paramref name="headSha"/> → <see cref="MergePendingIssue"/>, pinned to that head (ADR 0105).</item>
    /// <item><b>Fix:</b> <see cref="ChecksOutcome.StillFailing"/> naming a failing check whose script ran, under the
    /// fix cap → a <see cref="RevisionTrigger.FailedChecks"/> revision naming only such checks (issue #968).</item>
    /// <item><b>Review:</b> everything else → <see cref="ReviewIssue"/> carrying the outcome.</item>
    /// </list>
    /// A passed verdict with no head routes to review: a merge must be pinned to a commit. So does one on an issue
    /// whose revisions a person's review feedback drove (<see cref="Issue.HasPersonFeedback"/>, issue #917), so the
    /// person sees the fix before it lands.
    /// </summary>
    public Issue ResolveVerdict(
        ChecksOutcome checksOutcome,
        bool autoMerge,
        bool reviewPullRequests,
        string? headSha,
        DateTimeOffset transitionedAt)
    {
        if (reviewPullRequests
            && checksOutcome is (ChecksOutcome.Passed or ChecksOutcome.NoneDetected)
            && !string.IsNullOrWhiteSpace(headSha))
        {
            return QueueReview(checksOutcome, headSha, autoMerge, transitionedAt);
        }

        if (checksOutcome is ChecksOutcome.Passed passed
            && autoMerge
            && !HasPersonFeedback
            && !string.IsNullOrWhiteSpace(headSha))
        {
            return MarkMergePending(passed, headSha, transitionedAt);
        }

        if (checksOutcome is ChecksOutcome.StillFailing stillFailing && CanFixChecks(stillFailing.FailingChecks))
        {
            return FixChecks(stillFailing.FailingChecks, transitionedAt);
        }

        return ToReview(checksOutcome, headSha, transitionedAt);
    }

    private ReviewQueuedIssue QueueReview(
        ChecksOutcome checksOutcome,
        string headSha,
        bool autoMerge,
        DateTimeOffset transitionedAt)
    {
        ReviewQueuedIssue queued = ReviewQueuedIssue.FromChecksPending(
            this,
            checksOutcome,
            headSha,
            autoMerge,
            transitionedAt);
        AddDomainEvent(new Events.IssueReviewQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    private MergePendingIssue MarkMergePending(ChecksOutcome.Passed passed, string headSha, DateTimeOffset transitionedAt)
    {
        MergePendingIssue mergePending = MergePendingIssue.FromChecksPending(this, passed, headSha, transitionedAt);
        AddDomainEvent(new Events.IssueMergePending(
            Id,
            MonitoredRepositoryId,
            mergePending.IssueNumber,
            mergePending.EnteredMergePendingAt));
        return mergePending;
    }

    /// <summary>
    /// Resolves the gate to review. <paramref name="verifiedHeadSha"/> is the head commit the status read returned
    /// in the poll that resolved <paramref name="checksOutcome"/>, or <see langword="null"/> when it is unknown.
    /// </summary>
    public ReviewIssue ToReview(ChecksOutcome checksOutcome, string? verifiedHeadSha, DateTimeOffset transitionedAt)
    {
        ReviewIssue review = ReviewIssue.FromChecksPending(this, checksOutcome, verifiedHeadSha, transitionedAt);
        AddDomainEvent(new Events.IssueInReview(Id, MonitoredRepositoryId));
        return review;
    }

    public RevisionQueuedIssue Revise(
        IReadOnlyList<ReviewComment> comments,
        DateTimeOffset transitionedAt,
        int omittedCommentCount = 0,
        DateTimeOffset? newestCommentAt = null)
    {
        RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromChecksPending(
            this,
            comments,
            omittedCommentCount,
            newestCommentAt,
            transitionedAt);
        AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
        return revisionQueued;
    }

    /// <summary>
    /// Whether a fix run can address <paramref name="failingChecks"/>: at least one ran its script and failed, and the
    /// fix budget is not spent. Infrastructure failures (a job that never ran its script) give a fix run nothing to
    /// fix, so they neither start one nor spend an attempt.
    /// </summary>
    internal bool CanFixChecks(IReadOnlyList<FailingCheck> failingChecks) =>
        failingChecks.Any(IsFixable)
        && ChecksFixAttempts < ChecksMaxFixPushes;

    /// <summary>
    /// Queues a fix revision for the fixable checks among <paramref name="failingChecks"/>; infrastructure failures
    /// are left out of the revision.
    /// </summary>
    public RevisionQueuedIssue FixChecks(IReadOnlyList<FailingCheck> failingChecks, DateTimeOffset transitionedAt)
    {
        List<FailingCheck> fixable = [.. failingChecks.Where(IsFixable)];
        if (fixable.Count == 0)
        {
            throw new ArgumentException(
                "A checks fix must name at least one failing check whose script ran.",
                nameof(failingChecks));
        }

        RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromChecksPendingFix(this, fixable, transitionedAt);
        AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
        return revisionQueued;
    }

    private static bool IsFixable(FailingCheck check) => !check.IsInfrastructureFailure;

    public CompletedIssue Complete(DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromChecksPending(this, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }

    public ContinuableFailedIssue Fail(
        string failureReason,
        FailureCategoryVO failureCategory,
        DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromChecksPending(
            this,
            failureReason,
            failureCategory,
            failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
