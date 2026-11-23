using System.Diagnostics;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using FailureCategoryVO = Foundry.Modules.Workers.Contracts.FailureCategory;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// A review worker is judging the pull request at <see cref="ReviewedCommit"/> (ADR 0113). It holds a worker slot like
/// any running worker, and leaves by its run's verdict, or by a failed run: a transient failure with a retry left
/// waits as <see cref="ReviewFailedIssue"/>, and any other failure fails open to a person's review. No failure merges.
/// </summary>
public sealed class ReviewInProgressIssue : Issue, IReviewStepState
{
    public override IssueStage Stage => IssueStage.Review;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => false;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    public override IssueLifecycleStep LifecycleStep => IssueLifecycleStep.Review;

    public override RunKind? RunKind => ValueObjects.RunKind.Review;

    // Private parameterless constructor for EF Core materialization.
    private ReviewInProgressIssue()
    {
    }

    private ReviewInProgressIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    /// <summary>The review run.</summary>
    public WorkerRunId WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public override string? LinkedPullRequestUrl => NullIfEmpty(PullRequestUrl);

    public DateTimeOffset FeedbackCutoffAt { get; private set; }

    public ChecksOutcome ChecksOutcome { get; private set; } = new ChecksOutcome.Passed();

    public RebaseOutcome? RebaseOutcome { get; private set; }

    public int RebaseConflictAttempts { get; private set; }

    public string ReviewedCommit { get; private set; } = string.Empty;

    public bool AutoMerge { get; private set; }

    internal static ReviewInProgressIssue FromReviewQueued(
        ReviewQueuedIssue source,
        WorkerRunId workerRunId,
        DateTimeOffset claimedAt)
    {
        ReviewInProgressIssue inProgress = new(source.Id, claimedAt);
        inProgress.CopySharedPropertiesFrom(source);
        inProgress.WorkerRunId = workerRunId;
        inProgress.BranchName = source.BranchName;
        inProgress.PullRequestUrl = source.PullRequestUrl;
        inProgress.FeedbackCutoffAt = source.FeedbackCutoffAt;
        inProgress.ChecksOutcome = source.ChecksOutcome;
        inProgress.RebaseOutcome = source.RebaseOutcome;
        inProgress.RebaseConflictAttempts = source.RebaseConflictAttempts;
        inProgress.ReviewedCommit = source.ReviewedCommit;
        inProgress.AutoMerge = source.AutoMerge;
        return inProgress;
    }

    /// <summary>
    /// Routes the review's <paramref name="outcome"/> (ADR 0113):
    /// <list type="bullet">
    /// <item><b>Merge:</b> <see cref="ReviewVerdictOutcome.Approved"/> on a <see cref="ChecksOutcome.Passed"/> verdict
    /// with <see cref="AutoMerge"/> on and no person's feedback → <see cref="MergePendingIssue"/>, pinned to
    /// <see cref="ReviewedCommit"/>. A pull request without checks never merges itself (ADR 0105).</item>
    /// <item><b>Fix:</b> <see cref="ReviewVerdictOutcome.ChangesRequested"/> while the round has review fix runs left
    /// (<see cref="Issue.ReviewMaxFixRuns"/>) → a <see cref="RevisionTrigger.ReviewFindings"/> revision addressing
    /// <paramref name="findings"/>.</item>
    /// <item><b>Person:</b> everything else → <see cref="ReviewIssue"/>.</item>
    /// </list>
    /// The verdict is recorded as the issue's <see cref="Issue.LatestReview"/>, with the <paramref name="patchId"/> of
    /// the diff it judged, so a later review of an unchanged change can carry an approval.
    /// </summary>
    public Issue ResolveVerdict(
        ReviewVerdictOutcome outcome,
        string? patchId,
        RevisionTrigger.ReviewFindings findings,
        DateTimeOffset transitionedAt)
    {
        ArgumentNullException.ThrowIfNull(findings);

        if (!Enum.IsDefined(outcome))
        {
            throw new UnreachableException($"Unhandled review verdict outcome: {outcome}");
        }

        RecordLatestReview(new LatestReview(WorkerRunId, outcome, patchId));

        if (outcome is ReviewVerdictOutcome.ChangesRequested && ReviewFixAttempts < ReviewMaxFixRuns)
        {
            RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromReviewInProgress(this, findings, transitionedAt);
            AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
            return revisionQueued;
        }

        if (outcome is ReviewVerdictOutcome.Approved
            && ChecksOutcome is ChecksOutcome.Passed passed
            && AutoMerge
            && !HasPersonFeedback)
        {
            MergePendingIssue mergePending = MergePendingIssue.FromReviewInProgress(this, passed, transitionedAt);
            AddDomainEvent(new Events.IssueMergePending(
                Id,
                MonitoredRepositoryId,
                mergePending.IssueNumber,
                mergePending.EnteredMergePendingAt));
            return mergePending;
        }

        return ToReview(reviewOutcome: null, transitionedAt);
    }

    /// <summary>
    /// The review run failed. A failure whose category retries automatically, with
    /// <paramref name="consecutiveAutoRetryableFailures"/> (this failure included) short of its schedule, waits for that
    /// retry; any other failure skips the review and hands the green pull request to a person (ADR 0113).
    /// </summary>
    public Issue MarkRunFailed(
        string failureReason,
        FailureCategoryVO failureCategory,
        int consecutiveAutoRetryableFailures,
        DateTimeOffset failedAt)
    {
        if (failureCategory.AutomaticRetry is { } schedule && !schedule.IsExhausted(consecutiveAutoRetryableFailures))
        {
            ReviewFailedIssue failed = ReviewFailedIssue.FromReviewInProgress(
                this,
                failureReason,
                failureCategory,
                failedAt);
            AddDomainEvent(new Events.IssueReviewFailed(Id, MonitoredRepositoryId));
            return failed;
        }

        return ToReview(new ReviewOutcome.Skipped(failureCategory.Value), failedAt);
    }

    private ReviewIssue ToReview(ReviewOutcome? reviewOutcome, DateTimeOffset transitionedAt)
    {
        ReviewIssue review = ReviewIssue.FromReviewInProgress(this, reviewOutcome, transitionedAt);
        AddDomainEvent(new Events.IssueInReview(Id, MonitoredRepositoryId));
        return review;
    }

    /// <summary>A person merged the pull request (closing the issue) while the review ran.</summary>
    public CompletedIssue Complete(DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromReviewStep(this, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }

    /// <summary>The pull request closed without merging while the review ran.</summary>
    public ContinuableFailedIssue Fail(string failureReason, FailureCategoryVO failureCategory, DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromReviewStep(this, failureReason, failureCategory, failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
