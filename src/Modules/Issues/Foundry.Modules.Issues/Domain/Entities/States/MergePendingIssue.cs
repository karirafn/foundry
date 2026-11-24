using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;

using FailureCategoryVO = Foundry.Modules.Workers.Contracts.FailureCategory;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// The checks gate resolved <see cref="ChecksOutcome.Passed"/> on a repository that opted in to Merge When Checks
/// Pass, and Foundry is merging the pull request it opened, pinned to <see cref="HeadSha"/> (ADR 0105). A merged
/// pull request completes the issue; any other merge outcome sends it to review with a recorded
/// <see cref="MergeOutcome"/>.
/// </summary>
public sealed class MergePendingIssue : Issue
{
    public override IssueStage Stage => IssueStage.Merge;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => false;

    public override bool WaitsOnBlockers => false;

    public override string? TransitionCause => ChecksOutcome.Discriminator;

    public override IssueLifecycleStep LifecycleStep => IssueLifecycleStep.Merge;

    public override RunKind? RunKind => null;

    // Private parameterless constructor for EF Core materialization.
    private MergePendingIssue()
    {
    }

    private MergePendingIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public override string? LinkedPullRequestUrl => NullIfEmpty(PullRequestUrl);

    /// <summary>The head commit the checks gate verified; the merge is refused when the pull request moved on.</summary>
    public string HeadSha { get; private set; } = string.Empty;

    public ChecksOutcome ChecksOutcome { get; private set; } = new ChecksOutcome.Passed();

    /// <summary>How the post-run rebase resolved, carried from checks-pending (ADR 0087).</summary>
    public RebaseOutcome? RebaseOutcome { get; private set; }

    public int RebaseConflictAttempts { get; private set; }

    public DateTimeOffset FeedbackCutoffAt { get; private set; }

    public DateTimeOffset EnteredMergePendingAt { get; private set; }

    internal static MergePendingIssue FromChecksPending(
        ChecksPendingIssue source,
        ChecksOutcome.Passed checksOutcome,
        string headSha,
        DateTimeOffset transitionedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headSha);

        MergePendingIssue mergePending = new(source.Id, transitionedAt);
        mergePending.CopySharedPropertiesFrom(source);
        mergePending.WorkerRunId = source.WorkerRunId;
        mergePending.BranchName = source.BranchName;
        mergePending.PullRequestUrl = source.PullRequestUrl;
        mergePending.HeadSha = headSha;
        mergePending.ChecksOutcome = checksOutcome;
        mergePending.RebaseOutcome = source.RebaseOutcome;
        mergePending.RebaseConflictAttempts = source.RebaseConflictAttempts;
        mergePending.FeedbackCutoffAt = source.FeedbackCutoffAt;
        mergePending.EnteredMergePendingAt = transitionedAt;
        return mergePending;
    }

    /// <summary>
    /// The reviewer approved a pull request whose checks passed, on a repository with Merge When Checks Pass on
    /// (ADR 0113): the merge is pinned to the reviewed commit, so a push since the review is refused as moved.
    /// </summary>
    internal static MergePendingIssue FromReviewInProgress(
        ReviewInProgressIssue source,
        ChecksOutcome.Passed checksOutcome,
        DateTimeOffset transitionedAt)
    {
        MergePendingIssue mergePending = new(source.Id, transitionedAt);
        mergePending.CopySharedPropertiesFrom(source);
        mergePending.WorkerRunId = source.WorkerRunId;
        mergePending.BranchName = source.BranchName;
        mergePending.PullRequestUrl = source.PullRequestUrl;
        mergePending.HeadSha = source.ReviewedCommit;
        mergePending.ChecksOutcome = checksOutcome;
        mergePending.RebaseOutcome = source.RebaseOutcome;
        mergePending.RebaseConflictAttempts = source.RebaseConflictAttempts;
        mergePending.FeedbackCutoffAt = source.FeedbackCutoffAt;
        mergePending.EnteredMergePendingAt = transitionedAt;
        return mergePending;
    }

    /// <summary>The pull request merged: the issue is done.</summary>
    public CompletedIssue Complete(DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromMergePending(this, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }

    /// <summary>
    /// Foundry did not merge (<see cref="MergeOutcome.Disabled"/> or <see cref="MergeOutcome.Skipped"/>): the
    /// issue waits in review for a person, with the reason recorded.
    /// </summary>
    public ReviewIssue ToReview(MergeOutcome mergeOutcome, DateTimeOffset transitionedAt)
    {
        if (mergeOutcome is MergeOutcome.Merged)
        {
            throw new ArgumentException("A merged pull request completes the issue; it never goes to review.", nameof(mergeOutcome));
        }

        ReviewIssue review = ReviewIssue.FromMergePending(this, mergeOutcome, transitionedAt);
        AddDomainEvent(new Events.IssueInReview(Id, MonitoredRepositoryId));
        return review;
    }

    /// <summary>
    /// The merge was refused because the pull request fell behind the default branch, and it is the head of its
    /// repository's catch-up queue (ADR 0111): its branch rebases onto the default branch, then its checks run again
    /// before it merges.
    /// </summary>
    public RebasePendingIssue CatchUp(DateTimeOffset transitionedAt)
    {
        RebasePendingIssue rebasePending = RebasePendingIssue.FromMergePendingCatchUp(this, transitionedAt);
        AddDomainEvent(new Events.IssueRebasePending(
            Id,
            MonitoredRepositoryId,
            rebasePending.IssueNumber,
            rebasePending.EnteredRebasePendingAt));
        return rebasePending;
    }

    /// <summary>
    /// A person's review feedback arrived before Foundry merged (issue #917): the merge is abandoned and the feedback
    /// drives a revision, exactly as from checks-pending.
    /// </summary>
    public RevisionQueuedIssue Revise(
        IReadOnlyList<ReviewComment> comments,
        DateTimeOffset transitionedAt,
        int omittedCommentCount = 0,
        DateTimeOffset? newestCommentAt = null)
    {
        RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromMergePending(
            this,
            comments,
            omittedCommentCount,
            newestCommentAt,
            transitionedAt);
        AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
        return revisionQueued;
    }

    public ContinuableFailedIssue Fail(
        string failureReason,
        FailureCategoryVO failureCategory,
        DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromMergePending(
            this,
            failureReason,
            failureCategory,
            failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
