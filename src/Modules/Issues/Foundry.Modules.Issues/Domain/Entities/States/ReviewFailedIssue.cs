using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using FailureCategoryVO = Foundry.Modules.Workers.Contracts.FailureCategory;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// A review run failed transiently with an automatic retry left (ADR 0113). The transient retry service queues the
/// review of the same commit again once the category's backoff elapses. A failure with no retry left never lands here:
/// it fails open to a person's review instead, so this state always has an automatic exit.
/// </summary>
public sealed class ReviewFailedIssue : Issue, IReviewStepState, IAutoRetryableFailedState
{
    public override IssueStage Stage => IssueStage.Review;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => false;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    public override RunKind? RunKind => null;

    // Private parameterless constructor for EF Core materialization.
    private ReviewFailedIssue()
    {
    }

    private ReviewFailedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    /// <summary>The failed review run.</summary>
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

    public string FailureReason { get; private set; } = string.Empty;

    public FailureCategoryVO FailureCategory { get; private set; } = FailureCategoryVO.NonZeroExit;

    public DateTimeOffset FailedAt { get; private set; }

    internal static ReviewFailedIssue FromReviewInProgress(
        ReviewInProgressIssue source,
        string failureReason,
        FailureCategoryVO failureCategory,
        DateTimeOffset failedAt)
    {
        ReviewFailedIssue failed = new(source.Id, failedAt);
        failed.CopySharedPropertiesFrom(source);
        failed.WorkerRunId = source.WorkerRunId;
        failed.BranchName = source.BranchName;
        failed.PullRequestUrl = source.PullRequestUrl;
        failed.FeedbackCutoffAt = source.FeedbackCutoffAt;
        failed.ChecksOutcome = source.ChecksOutcome;
        failed.RebaseOutcome = source.RebaseOutcome;
        failed.RebaseConflictAttempts = source.RebaseConflictAttempts;
        failed.ReviewedCommit = source.ReviewedCommit;
        failed.AutoMerge = source.AutoMerge;
        failed.FailureReason = failureReason;
        failed.FailureCategory = failureCategory;
        failed.FailedAt = failedAt;
        return failed;
    }

    /// <summary>Retries the review: the issue waits for a review worker again, for the same commit.</summary>
    public ReviewQueuedIssue Retry(DateTimeOffset transitionedAt)
    {
        ReviewQueuedIssue queued = ReviewQueuedIssue.FromReviewFailed(this, transitionedAt);
        AddDomainEvent(new Events.IssueReviewQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    /// <summary>A person merged the pull request (closing the issue) while the review waited for its retry.</summary>
    public CompletedIssue Complete(DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromReviewStep(this, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }

    /// <summary>The pull request closed without merging while the review waited for its retry.</summary>
    public ContinuableFailedIssue Fail(string failureReason, FailureCategoryVO failureCategory, DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromReviewStep(this, failureReason, failureCategory, failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
