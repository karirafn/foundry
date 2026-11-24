using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// A queued continuation whose issue has open blockers: an issue with open blockers starts no implement run. It holds
/// the branch and failure reason its <see cref="ContinuationQueuedIssue"/> carried, so when the blockers close it
/// unblocks only to that queued continuation (as <see cref="TriageBlockedIssue"/> does for triage, ADR 0102).
/// It is not a <see cref="QueuedIssue"/>: it is never a dispatch candidate and holds no worker slot.
/// </summary>
public sealed class ContinuationBlockedIssue : Issue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => true;

    public override bool WaitsOnBlockers => true;

    public override IssueCodeStep? CodeStep => null;

    public override string? TransitionCause => null;

    public override IssueLifecycleStep LifecycleStep => IssueLifecycleStep.Queued;

    public override RunKind? RunKind => ValueObjects.RunKind.Continuation;

    // Private parameterless constructor for EF Core materialization.
    private ContinuationBlockedIssue()
    {
    }

    private ContinuationBlockedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public string BranchName { get; private set; } = string.Empty;

    public string FailureReason { get; private set; } = string.Empty;

    /// <inheritdoc cref="QueuedIssue.ContinuedWithoutAnswer"/>
    public bool ContinuedWithoutAnswer { get; private set; }

    internal static ContinuationBlockedIssue FromContinuationQueued(
        ContinuationQueuedIssue source,
        IReadOnlyList<int> blockers,
        DateTimeOffset transitionedAt)
    {
        if (blockers.Count == 0)
        {
            throw new InvalidOperationException("Cannot block an issue without specifying at least one blocker.");
        }

        ContinuationBlockedIssue blocked = new(source.Id, transitionedAt);
        blocked.CopySharedPropertiesFrom(source);
        blocked.SetBlockedBy(blockers);
        blocked.BranchName = source.BranchName;
        blocked.FailureReason = source.FailureReason;
        blocked.ContinuedWithoutAnswer = source.ContinuedWithoutAnswer;
        return blocked;
    }

    public ContinuationQueuedIssue Unblock(DateTimeOffset transitionedAt)
    {
        ContinuationQueuedIssue queued = ContinuationQueuedIssue.FromContinuationBlocked(this, transitionedAt);
        AddDomainEvent(new Events.IssueContinuationQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
