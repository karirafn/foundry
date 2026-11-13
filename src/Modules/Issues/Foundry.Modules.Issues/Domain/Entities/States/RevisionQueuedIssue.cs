using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using BranchNameValue = Foundry.Shared.BranchName;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class RevisionQueuedIssue : QueuedIssue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => true;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private RevisionQueuedIssue()
    {
    }

    private RevisionQueuedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public override int TierRank => 0;

    public override BranchNameValue? DispatchBranchName => BranchNameValue.From(BranchName);

    public override DispatchContext Context =>
        new DispatchContext.Revision(BranchName, PullRequestUrl, Trigger);

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public override string? LinkedPullRequestUrl => NullIfEmpty(PullRequestUrl);

    // Placeholder replaced by the factory method or EF materialisation; invalid until assigned.
    public RevisionTrigger Trigger { get; private set; } = new RevisionTrigger.ReviewFeedback([], 0, null);

    public int ChecksFixAttempts { get; private set; }

    public int RebaseConflictAttempts { get; private set; }

    internal static RevisionQueuedIssue FromReview(
        ReviewIssue source,
        IReadOnlyList<ReviewComment> comments,
        int omittedCommentCount,
        DateTimeOffset? newestCommentAt,
        DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = new RevisionTrigger.ReviewFeedback(comments, omittedCommentCount, newestCommentAt);
        revisionQueued.ChecksFixAttempts = 0;
        revisionQueued.RebaseConflictAttempts = source.RebaseConflictAttempts;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromChecksPending(
        ChecksPendingIssue source,
        IReadOnlyList<ReviewComment> comments,
        int omittedCommentCount,
        DateTimeOffset? newestCommentAt,
        DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = new RevisionTrigger.ReviewFeedback(comments, omittedCommentCount, newestCommentAt);
        revisionQueued.ChecksFixAttempts = 0;
        revisionQueued.RebaseConflictAttempts = source.RebaseConflictAttempts;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromChecksPendingFix(
        ChecksPendingIssue source,
        IReadOnlyList<FailingCheck> failingChecks,
        DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = new RevisionTrigger.FailedChecks(failingChecks);
        revisionQueued.ChecksFixAttempts = source.ChecksFixAttempts + 1;
        revisionQueued.RebaseConflictAttempts = source.RebaseConflictAttempts;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromRebaseConflict(RebasePendingIssue source, DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = new RevisionTrigger.RebaseConflict();
        revisionQueued.ChecksFixAttempts = source.ChecksFixAttempts;
        revisionQueued.RebaseConflictAttempts = source.RebaseConflictAttempts + 1;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromRevisionFailed(RevisionFailedIssue source, DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = source.Trigger;
        revisionQueued.ChecksFixAttempts = source.ChecksFixAttempts;
        revisionQueued.RebaseConflictAttempts = source.RebaseConflictAttempts;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromAwaitingInput(
        AwaitingInputIssue source,
        ResumeTarget.Revision target,
        string input,
        DateTimeOffset answeredAt,
        DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id, transitionedAt);
        revisionQueued.CopySharedPropertiesFrom(source);
        revisionQueued.AnswerClarification(input, answeredAt);
        revisionQueued.BranchName = target.BranchName;
        revisionQueued.PullRequestUrl = target.PullRequestUrl;
        revisionQueued.Trigger = new RevisionTrigger.InputProvided();
        revisionQueued.ChecksFixAttempts = 0;
        revisionQueued.RebaseConflictAttempts = 0;
        return revisionQueued;
    }

    public override RevisionInProgressIssue Claim(WorkerRunId workerRunId, DateTimeOffset claimedAt)
    {
        RevisionInProgressIssue revisionInProgress = RevisionInProgressIssue.FromRevisionQueued(
            this,
            workerRunId,
            claimedAt);
        AddDomainEvent(new Events.IssueRevisionInProgress(Id, MonitoredRepositoryId));
        return revisionInProgress;
    }
}
