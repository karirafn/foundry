using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using FailureCategoryVO = Foundry.Modules.Workers.Contracts.FailureCategory;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// A worker run left an open pull request, or a merge was refused because the pull request fell behind (ADR 0111),
/// and Foundry is rebasing its branch onto the default branch
/// through the provider (ADR 0087). The issue leaves this state for checks-pending once the rebase step
/// resolves a <see cref="RebaseOutcome"/>, so no checks verdict is ever read against the pre-rebase head.
/// </summary>
public sealed class RebasePendingIssue : Issue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => false;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => IssueCodeStep.Rebasing;

    /// <summary>
    /// Maximum number of consecutive <see cref="RevisionTrigger.RebaseConflict"/> revisions Foundry dispatches before a
    /// further conflict fails open to checks-pending with <see cref="RebaseOutcome.Conflict"/>.
    /// </summary>
    public const int MaxRebaseConflictAttempts = 2;

    // Private parameterless constructor for EF Core materialization.
    private RebasePendingIssue()
    {
    }

    private RebasePendingIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public override string? LinkedPullRequestUrl => NullIfEmpty(PullRequestUrl);

    public DateTimeOffset EnteredRebasePendingAt { get; private set; }

    public DateTimeOffset FeedbackCutoffAt { get; private set; }

    public int ChecksFixAttempts { get; private set; }

    /// <summary>
    /// Consecutive rebase conflicts that dispatched a conflict-resolution revision. Reset by a successful sync
    /// (<see cref="RebaseOutcome.UpToDate"/> or <see cref="RebaseOutcome.Rebased"/>); left unchanged by
    /// <see cref="RebaseOutcome.Disabled"/> and <see cref="RebaseOutcome.Skipped"/>.
    /// </summary>
    public int RebaseConflictAttempts { get; private set; }

    /// <summary>
    /// Why the branch is rebasing. <see cref="RebasePendingReason.AfterRun"/> for rows written before the reason
    /// existed.
    /// </summary>
    public RebasePendingReason Reason { get; private set; } = new RebasePendingReason.AfterRun();

    /// <summary>
    /// For a <see cref="RebasePendingReason.CatchUp"/> rebase, the head commit the refused merge was pinned to; a
    /// catch-up that fails sends the issue to review verified at that head. <see langword="null"/> otherwise.
    /// </summary>
    public string? CatchUpHeadSha { get; private set; }

    internal static RebasePendingIssue FromInProgress(
        InProgressIssue source,
        string branchName,
        string pullRequestUrl,
        DateTimeOffset feedbackCutoffAt,
        RebasePendingReason.AfterRun reason,
        DateTimeOffset transitionedAt)
    {
        RebasePendingIssue rebasePending = new(source.Id, transitionedAt);
        rebasePending.CopySharedPropertiesFrom(source);
        rebasePending.WorkerRunId = source.WorkerRunId;
        rebasePending.BranchName = branchName;
        rebasePending.PullRequestUrl = pullRequestUrl;
        rebasePending.EnteredRebasePendingAt = DateTimeOffset.UtcNow;
        rebasePending.FeedbackCutoffAt = feedbackCutoffAt;
        rebasePending.ChecksFixAttempts = 0;
        rebasePending.RebaseConflictAttempts = 0;
        rebasePending.Reason = reason;
        return rebasePending;
    }

    internal static RebasePendingIssue FromRevisionInProgress(
        RevisionInProgressIssue source,
        RebasePendingReason.AfterRun reason,
        DateTimeOffset feedbackCutoffAt,
        DateTimeOffset transitionedAt)
    {
        RebasePendingIssue rebasePending = new(source.Id, transitionedAt);
        rebasePending.CopySharedPropertiesFrom(source);
        rebasePending.WorkerRunId = source.WorkerRunId;
        rebasePending.BranchName = source.BranchName;
        rebasePending.PullRequestUrl = source.PullRequestUrl;
        rebasePending.EnteredRebasePendingAt = DateTimeOffset.UtcNow;
        rebasePending.FeedbackCutoffAt = feedbackCutoffAt;
        rebasePending.ChecksFixAttempts = source.ChecksFixAttempts;
        rebasePending.RebaseConflictAttempts = source.RebaseConflictAttempts;
        rebasePending.Reason = reason;
        return rebasePending;
    }

    /// <summary>
    /// The merge-pending issue is the head of its repository's catch-up queue (ADR 0111): its branch rebases onto
    /// the default branch, then its checks run again before it merges. A merge-pending issue carries no checks-fix
    /// count, so a red verdict after the catch-up starts from a fresh fix budget.
    /// </summary>
    internal static RebasePendingIssue FromMergePendingCatchUp(MergePendingIssue source, DateTimeOffset transitionedAt)
    {
        RebasePendingIssue rebasePending = new(source.Id, transitionedAt);
        rebasePending.CopySharedPropertiesFrom(source);
        rebasePending.WorkerRunId = source.WorkerRunId;
        rebasePending.BranchName = source.BranchName;
        rebasePending.PullRequestUrl = source.PullRequestUrl;
        rebasePending.EnteredRebasePendingAt = transitionedAt;
        rebasePending.FeedbackCutoffAt = source.FeedbackCutoffAt;
        rebasePending.ChecksFixAttempts = 0;
        rebasePending.RebaseConflictAttempts = source.RebaseConflictAttempts;
        rebasePending.Reason = new RebasePendingReason.CatchUp();
        rebasePending.CatchUpHeadSha = source.HeadSha;
        return rebasePending;
    }

    /// <summary>
    /// Applies the rebase step's <paramref name="rebaseOutcome"/>:
    /// <list type="bullet">
    /// <item>A <see cref="RebaseOutcome.Conflict"/> below <see cref="MaxRebaseConflictAttempts"/> queues a
    /// conflict-resolution revision, whatever the <see cref="Reason"/>.</item>
    /// <item>A <see cref="RebasePendingReason.CatchUp"/> rebase that did not bring the branch up to date
    /// (<see cref="RebaseOutcome.Skipped"/>, <see cref="RebaseOutcome.Disabled"/>, or a conflict at the cap) goes to
    /// review as <c>skipped(catch_up_failed)</c>: without the rebase the merge would be refused again.</item>
    /// <item>Anything else moves on to the checks gate; an <see cref="RebasePendingReason.AfterRun"/> conflict at the
    /// cap fails open there.</item>
    /// </list>
    /// </summary>
    public Issue ResolveRebase(RebaseOutcome rebaseOutcome, DateTimeOffset transitionedAt)
    {
        if (rebaseOutcome is RebaseOutcome.Conflict && RebaseConflictAttempts < MaxRebaseConflictAttempts)
        {
            return ResolveConflict(transitionedAt);
        }

        if (Reason is RebasePendingReason.CatchUp && !IsSynced(rebaseOutcome))
        {
            return FailCatchUp(rebaseOutcome, transitionedAt);
        }

        return MarkChecksPending(rebaseOutcome, transitionedAt);
    }

    /// <summary>
    /// The rebase conflicted: queues a <see cref="RevisionTrigger.RebaseConflict"/> revision and counts the attempt.
    /// </summary>
    public RevisionQueuedIssue ResolveConflict(DateTimeOffset transitionedAt)
    {
        if (RebaseConflictAttempts >= MaxRebaseConflictAttempts)
        {
            throw new InvalidOperationException(
                $"Rebase conflict attempts ({RebaseConflictAttempts}) already reached the cap ({MaxRebaseConflictAttempts}).");
        }

        RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromRebaseConflict(this, transitionedAt);
        AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
        return revisionQueued;
    }

    /// <summary>
    /// A catch-up rebase could not bring the branch up to date: the issue waits in review with its checks passed,
    /// the merge recorded as <c>skipped(catch_up_failed)</c> and the rebase's own outcome kept.
    /// </summary>
    private ReviewIssue FailCatchUp(RebaseOutcome rebaseOutcome, DateTimeOffset transitionedAt)
    {
        ReviewIssue review = ReviewIssue.FromFailedCatchUp(this, rebaseOutcome, transitionedAt);
        AddDomainEvent(new Events.IssueInReview(Id, MonitoredRepositoryId));
        return review;
    }

    private static bool IsSynced(RebaseOutcome rebaseOutcome) =>
        rebaseOutcome is RebaseOutcome.UpToDate or RebaseOutcome.Rebased;

    /// <summary>
    /// The rebase step resolved with <paramref name="rebaseOutcome"/>: the issue moves on to the checks gate,
    /// which now reads checks against the (possibly rebased) head. A catch-up only moves on once its branch is up
    /// to date.
    /// </summary>
    public ChecksPendingIssue MarkChecksPending(RebaseOutcome rebaseOutcome, DateTimeOffset transitionedAt)
    {
        if (Reason is RebasePendingReason.CatchUp && !IsSynced(rebaseOutcome))
        {
            throw new InvalidOperationException(
                $"A catch-up rebase that resolved '{rebaseOutcome.Discriminator}' goes to review, not to the checks gate.");
        }

        ChecksPendingIssue checksPending = ChecksPendingIssue.FromRebasePending(this, rebaseOutcome, transitionedAt);
        AddDomainEvent(new Events.IssueChecksPending(
            Id,
            MonitoredRepositoryId,
            checksPending.IssueNumber,
            checksPending.EnteredChecksPendingAt));
        return checksPending;
    }

    public CompletedIssue Complete(DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromRebasePending(this, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }

    public ContinuableFailedIssue Fail(
        string failureReason,
        FailureCategoryVO failureCategory,
        DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromRebasePending(
            this,
            failureReason,
            failureCategory,
            failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
