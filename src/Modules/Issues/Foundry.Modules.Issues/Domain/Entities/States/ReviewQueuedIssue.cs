using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using BranchNameValue = Foundry.Shared.BranchName;
using FailureCategoryVO = Foundry.Modules.Workers.Contracts.FailureCategory;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// A green pull request waiting for a worker to review it (ADR 0113): its checks gate resolved
/// <see cref="ChecksOutcome.Passed"/> or <see cref="ChecksOutcome.NoneDetected"/> on a repository with Review Pull
/// Requests on. It is dispatched ahead of every run except a revision, because a review only judges work already done.
/// </summary>
public sealed class ReviewQueuedIssue : QueuedIssue, IReviewStepState
{
    public override IssueStage Stage => IssueStage.Review;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => true;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    public override RunKind? RunKind => ValueObjects.RunKind.Review;

    // Private parameterless constructor for EF Core materialization.
    private ReviewQueuedIssue()
    {
    }

    private ReviewQueuedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public override int TierRank => 1;

    /// <summary>A review run pushes nothing, so it gets no branch; its context names the branch it reads.</summary>
    public override BranchNameValue? DispatchBranchName => null;

    /// <summary>
    /// Carries the patch-id of the issue's latest approval, so dispatch can complete the review as carried when the
    /// change is unchanged since that approval (ADR 0113).
    /// </summary>
    public override DispatchContext Context =>
        new DispatchContext.Review(BranchName, PullRequestUrl, ReviewedCommit, LatestReview?.CarriablePatchId);

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

    internal static ReviewQueuedIssue FromChecksPending(
        ChecksPendingIssue source,
        ChecksOutcome checksOutcome,
        string reviewedCommit,
        bool autoMerge,
        DateTimeOffset transitionedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedCommit);
        if (checksOutcome is not (ChecksOutcome.Passed or ChecksOutcome.NoneDetected))
        {
            throw new ArgumentException(
                "Only a green checks verdict (passed or none detected) is reviewed.",
                nameof(checksOutcome));
        }

        ReviewQueuedIssue queued = new(source.Id, transitionedAt);
        queued.CopySharedPropertiesFrom(source);
        queued.WorkerRunId = source.WorkerRunId;
        queued.BranchName = source.BranchName;
        queued.PullRequestUrl = source.PullRequestUrl;
        queued.FeedbackCutoffAt = source.FeedbackCutoffAt;
        queued.ChecksOutcome = checksOutcome;
        queued.RebaseOutcome = source.RebaseOutcome;
        queued.RebaseConflictAttempts = source.RebaseConflictAttempts;
        queued.ReviewedCommit = reviewedCommit;
        queued.AutoMerge = autoMerge;
        return queued;
    }

    /// <summary>A transient review failure retries the review of the same commit.</summary>
    internal static ReviewQueuedIssue FromReviewFailed(ReviewFailedIssue source, DateTimeOffset transitionedAt)
    {
        ReviewQueuedIssue queued = new(source.Id, transitionedAt);
        queued.CopySharedPropertiesFrom(source);
        queued.WorkerRunId = source.WorkerRunId;
        queued.BranchName = source.BranchName;
        queued.PullRequestUrl = source.PullRequestUrl;
        queued.FeedbackCutoffAt = source.FeedbackCutoffAt;
        queued.ChecksOutcome = source.ChecksOutcome;
        queued.RebaseOutcome = source.RebaseOutcome;
        queued.RebaseConflictAttempts = source.RebaseConflictAttempts;
        queued.ReviewedCommit = source.ReviewedCommit;
        queued.AutoMerge = source.AutoMerge;
        return queued;
    }

    /// <summary>A person retries a review that failed open, for the commit the skipped review was given.</summary>
    internal static ReviewQueuedIssue FromSkippedReview(
        ReviewIssue source,
        string reviewedCommit,
        DateTimeOffset transitionedAt)
    {
        ReviewQueuedIssue queued = new(source.Id, transitionedAt);
        queued.CopySharedPropertiesFrom(source);
        queued.WorkerRunId = source.WorkerRunId;
        queued.BranchName = source.BranchName;
        queued.PullRequestUrl = source.PullRequestUrl;
        queued.FeedbackCutoffAt = source.FeedbackCutoffAt;
        queued.ChecksOutcome = source.ChecksOutcome;
        queued.RebaseOutcome = source.RebaseOutcome;
        queued.RebaseConflictAttempts = source.RebaseConflictAttempts;
        queued.ReviewedCommit = reviewedCommit;
        queued.AutoMerge = source.AutoMerge;
        return queued;
    }

    public override ReviewInProgressIssue Claim(WorkerRunId workerRunId, DateTimeOffset claimedAt)
    {
        ReviewInProgressIssue inProgress = ReviewInProgressIssue.FromReviewQueued(this, workerRunId, claimedAt);
        AddDomainEvent(new Events.IssueReviewInProgress(Id, MonitoredRepositoryId));
        return inProgress;
    }

    /// <summary>A person merged the pull request (closing the issue) before the review ran.</summary>
    public CompletedIssue Complete(DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromReviewStep(this, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }

    /// <summary>The pull request closed without merging before the review ran.</summary>
    public ContinuableFailedIssue Fail(string failureReason, FailureCategoryVO failureCategory, DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromReviewStep(this, failureReason, failureCategory, failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
