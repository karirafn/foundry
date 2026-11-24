using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// A queued revision whose issue has open blockers: an issue with open blockers starts no implement run. It holds
/// everything its <see cref="RevisionQueuedIssue"/> carried, so when the blockers close it unblocks only to that queued
/// revision, with the same trigger, feedback cutoff and attempt counters (as <see cref="TriageBlockedIssue"/> does for
/// triage, ADR 0102). Its pull request closing fails it as continuable.
/// It is not a <see cref="QueuedIssue"/>: it is never a dispatch candidate and holds no worker slot.
/// </summary>
public sealed class RevisionBlockedIssue : Issue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => true;

    public override bool WaitsOnBlockers => true;

    public override IssueCodeStep? CodeStep => null;

    public override string? TransitionCause => Trigger.Discriminator;

    public override IssueLifecycleStep LifecycleStep => IssueLifecycleStep.Queued;

    public override RunKind? RunKind => RevisionRunKind.Of(Trigger);

    // Private parameterless constructor for EF Core materialization.
    private RevisionBlockedIssue()
    {
    }

    private RevisionBlockedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public override string? LinkedPullRequestUrl => NullIfEmpty(PullRequestUrl);

    // Placeholder replaced by the factory method or EF materialisation; invalid until assigned.
    public RevisionTrigger Trigger { get; private set; } = new RevisionTrigger.ReviewFeedback([], 0, null);

    /// <inheritdoc cref="RevisionQueuedIssue.PriorFeedbackCutoffAt"/>
    public DateTimeOffset? PriorFeedbackCutoffAt { get; private set; }

    public int RebaseConflictAttempts { get; private set; }

    /// <inheritdoc cref="QueuedIssue.ContinuedWithoutAnswer"/>
    public bool ContinuedWithoutAnswer { get; private set; }

    internal static RevisionBlockedIssue FromRevisionQueued(
        RevisionQueuedIssue source,
        IReadOnlyList<int> blockers,
        DateTimeOffset transitionedAt)
    {
        if (blockers.Count == 0)
        {
            throw new InvalidOperationException("Cannot block an issue without specifying at least one blocker.");
        }

        RevisionBlockedIssue blocked = new(source.Id, transitionedAt);
        blocked.CopySharedPropertiesFrom(source);
        blocked.SetBlockedBy(blockers);
        blocked.BranchName = source.BranchName;
        blocked.PullRequestUrl = source.PullRequestUrl;
        blocked.Trigger = source.Trigger;
        blocked.PriorFeedbackCutoffAt = source.PriorFeedbackCutoffAt;
        blocked.RebaseConflictAttempts = source.RebaseConflictAttempts;
        blocked.ContinuedWithoutAnswer = source.ContinuedWithoutAnswer;
        return blocked;
    }

    public RevisionQueuedIssue Unblock(DateTimeOffset transitionedAt)
    {
        RevisionQueuedIssue queued = RevisionQueuedIssue.FromRevisionBlocked(this, transitionedAt);
        AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    /// <summary>The pull request the held revision would revise was closed without merging.</summary>
    public ContinuableFailedIssue Fail(string failureReason, FailureCategory failureCategory, DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromRevisionBlocked(
            this,
            failureReason,
            failureCategory,
            failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
