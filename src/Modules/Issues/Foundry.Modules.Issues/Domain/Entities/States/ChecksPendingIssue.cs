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

    public int ChecksFixAttempts { get; private set; }

    public int RebaseConflictAttempts { get; private set; }

    /// <summary>
    /// How the post-run rebase resolved before the issue entered the checks gate (ADR 0087).
    /// <see langword="null"/> for issues that entered checks-pending before the rebase step existed.
    /// </summary>
    public RebaseOutcome? RebaseOutcome { get; private set; }

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
        checksPending.ChecksFixAttempts = source.ChecksFixAttempts;
        checksPending.RebaseConflictAttempts = rebaseOutcome is RebaseOutcome.UpToDate or RebaseOutcome.Rebased
            ? 0
            : source.RebaseConflictAttempts;
        checksPending.RebaseOutcome = rebaseOutcome;
        return checksPending;
    }

    /// <summary>
    /// New commits reached a review issue's pull request: the checks gate starts over with a fresh fix budget,
    /// because every new budget follows a person's push, the same rule review feedback follows.
    /// </summary>
    internal static ChecksPendingIssue FromReview(ReviewIssue source, DateTimeOffset transitionedAt)
    {
        ChecksPendingIssue checksPending = new(source.Id, transitionedAt);
        checksPending.CopySharedPropertiesFrom(source);
        checksPending.WorkerRunId = source.WorkerRunId;
        checksPending.BranchName = source.BranchName;
        checksPending.PullRequestUrl = source.PullRequestUrl;
        checksPending.EnteredChecksPendingAt = transitionedAt;
        checksPending.FeedbackCutoffAt = source.FeedbackCutoffAt;
        checksPending.ChecksFixAttempts = 0;
        checksPending.RebaseConflictAttempts = source.RebaseConflictAttempts;
        checksPending.RebaseOutcome = source.RebaseOutcome;
        return checksPending;
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

    internal bool CanFixChecks(IReadOnlyList<FailingCheck> failingChecks) =>
        failingChecks.Count > 0
        && ChecksFixAttempts < ChecksMaxFixPushes;

    public RevisionQueuedIssue FixChecks(IReadOnlyList<FailingCheck> failingChecks, DateTimeOffset transitionedAt)
    {
        if (failingChecks.Count == 0)
        {
            throw new ArgumentException("A checks fix must name at least one failing check.", nameof(failingChecks));
        }

        RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromChecksPendingFix(this, failingChecks, transitionedAt);
        AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
        return revisionQueued;
    }

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
