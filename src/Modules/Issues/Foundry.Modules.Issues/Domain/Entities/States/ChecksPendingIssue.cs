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
    /// <summary>
    /// Maximum number of FailedChecks revision containers Foundry will dispatch across the lifetime
    /// of an issue before transitioning it back to review instead of retrying.
    /// Distinct from <c>SystemPromptBuilder.ChecksMaxFixPushes</c>, which is the per-container
    /// in-run fix-push budget instructed to the worker.
    /// </summary>
    public const int ChecksMaxFixPushes = 2;

    // Private parameterless constructor for EF Core materialization.
    private ChecksPendingIssue()
    {
    }

    private ChecksPendingIssue(IssueId id) : base(id)
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
        RebaseOutcome rebaseOutcome)
    {
        ChecksPendingIssue checksPending = new(source.Id);
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

    public ReviewIssue ToReview(ChecksOutcome checksOutcome)
    {
        ReviewIssue review = ReviewIssue.FromChecksPending(this, checksOutcome);
        AddDomainEvent(new Events.IssueInReview(Id, MonitoredRepositoryId));
        return review;
    }

    public RevisionQueuedIssue Revise(
        IReadOnlyList<ReviewComment> comments,
        int omittedCommentCount = 0,
        DateTimeOffset? newestCommentAt = null)
    {
        RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromChecksPending(
            this,
            comments,
            omittedCommentCount,
            newestCommentAt);
        AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
        return revisionQueued;
    }

    public RevisionQueuedIssue FixChecks(IReadOnlyList<FailingCheck> failingChecks)
    {
        RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromChecksPendingFix(this, failingChecks);
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
