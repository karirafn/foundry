using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using FailureCategoryVO = Foundry.Modules.Workers.Contracts.FailureCategory;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// A worker run left an open pull request and Foundry is rebasing its branch onto the default branch
/// through the provider (ADR 0087). The issue leaves this state for checks-pending once the rebase step
/// resolves a <see cref="RebaseOutcome"/>, so no checks verdict is ever read against the pre-rebase head.
/// </summary>
public sealed class RebasePendingIssue : Issue
{
    /// <summary>
    /// Maximum number of consecutive <see cref="RevisionTrigger.RebaseConflict"/> revisions Foundry dispatches before a
    /// further conflict fails open to checks-pending with <see cref="RebaseOutcome.Conflict"/>.
    /// </summary>
    public const int MaxRebaseConflictAttempts = 2;

    // Private parameterless constructor for EF Core materialization.
    private RebasePendingIssue()
    {
    }

    private RebasePendingIssue(IssueId id) : base(id)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public DateTimeOffset EnteredRebasePendingAt { get; private set; }

    public DateTimeOffset FeedbackCutoffAt { get; private set; }

    public int ChecksFixAttempts { get; private set; }

    /// <summary>
    /// Consecutive rebase conflicts that dispatched a conflict-resolution revision. Reset by a successful sync
    /// (<see cref="RebaseOutcome.UpToDate"/> or <see cref="RebaseOutcome.Rebased"/>); left unchanged by
    /// <see cref="RebaseOutcome.Disabled"/> and <see cref="RebaseOutcome.Skipped"/>.
    /// </summary>
    public int RebaseConflictAttempts { get; private set; }

    internal static RebasePendingIssue FromInProgress(
        InProgressIssue source,
        string branchName,
        string pullRequestUrl,
        DateTimeOffset feedbackCutoffAt)
    {
        RebasePendingIssue rebasePending = new(source.Id);
        rebasePending.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        rebasePending.WorkerRunId = source.WorkerRunId;
        rebasePending.BranchName = branchName;
        rebasePending.PullRequestUrl = pullRequestUrl;
        rebasePending.EnteredRebasePendingAt = DateTimeOffset.UtcNow;
        rebasePending.FeedbackCutoffAt = feedbackCutoffAt;
        rebasePending.ChecksFixAttempts = 0;
        rebasePending.RebaseConflictAttempts = 0;
        return rebasePending;
    }

    internal static RebasePendingIssue FromRevisionInProgress(
        RevisionInProgressIssue source,
        DateTimeOffset feedbackCutoffAt)
    {
        RebasePendingIssue rebasePending = new(source.Id);
        rebasePending.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        rebasePending.WorkerRunId = source.WorkerRunId;
        rebasePending.BranchName = source.BranchName;
        rebasePending.PullRequestUrl = source.PullRequestUrl;
        rebasePending.EnteredRebasePendingAt = DateTimeOffset.UtcNow;
        rebasePending.FeedbackCutoffAt = feedbackCutoffAt;
        rebasePending.ChecksFixAttempts = source.ChecksFixAttempts;
        rebasePending.RebaseConflictAttempts = source.RebaseConflictAttempts;
        return rebasePending;
    }

    /// <summary>
    /// Applies the rebase step's <paramref name="rebaseOutcome"/>: a <see cref="RebaseOutcome.Conflict"/> below
    /// <see cref="MaxRebaseConflictAttempts"/> queues a conflict-resolution revision; anything else (including a
    /// conflict at the cap, which fails open) moves on to the checks gate.
    /// </summary>
    public Issue ResolveRebase(RebaseOutcome rebaseOutcome) =>
        rebaseOutcome is RebaseOutcome.Conflict && RebaseConflictAttempts < MaxRebaseConflictAttempts
            ? ResolveConflict()
            : MarkChecksPending(rebaseOutcome);

    /// <summary>
    /// The rebase conflicted: queues a <see cref="RevisionTrigger.RebaseConflict"/> revision and counts the attempt.
    /// </summary>
    public RevisionQueuedIssue ResolveConflict()
    {
        if (RebaseConflictAttempts >= MaxRebaseConflictAttempts)
        {
            throw new InvalidOperationException(
                $"Rebase conflict attempts ({RebaseConflictAttempts}) already reached the cap ({MaxRebaseConflictAttempts}).");
        }

        RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromRebaseConflict(this);
        AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
        return revisionQueued;
    }

    /// <summary>
    /// The rebase step resolved with <paramref name="rebaseOutcome"/>: the issue moves on to the checks gate,
    /// which now reads checks against the (possibly rebased) head.
    /// </summary>
    public ChecksPendingIssue MarkChecksPending(RebaseOutcome rebaseOutcome)
    {
        ChecksPendingIssue checksPending = ChecksPendingIssue.FromRebasePending(this, rebaseOutcome);
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
