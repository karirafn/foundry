using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using BranchNameValue = Foundry.Shared.BranchName;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class ContinuationQueuedIssue : QueuedIssue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => true;

    public override bool WaitsOnBlockers => false;

    public override string? TransitionCause => null;

    public override IssueLifecycleStep LifecycleStep => IssueLifecycleStep.Queued;

    public override RunKind? RunKind => ValueObjects.RunKind.Continuation;

    public const int FailureReasonMaxLength = 500;

    // Private parameterless constructor for EF Core materialization.
    private ContinuationQueuedIssue()
    {
    }

    private ContinuationQueuedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public override int TierRank => 2;

    public override BranchNameValue? DispatchBranchName => BranchNameValue.From(BranchName);

    public override DispatchContext Context =>
        new DispatchContext.Continuation(BranchName, FailureReason.Length == 0 ? null : FailureReason)
        {
            ContinuedWithoutAnswer = ContinuedWithoutAnswer,
        };

    public string BranchName { get; private set; } = string.Empty;

    public string FailureReason { get; private set; } = string.Empty;

    internal static ContinuationQueuedIssue FromContinuableFailed(
        ContinuableFailedIssue source,
        DateTimeOffset transitionedAt)
    {
        ContinuationQueuedIssue queued = new(source.Id, transitionedAt);
        queued.CopySharedPropertiesFrom(source);
        queued.BranchName = source.BranchName;
        queued.FailureReason = source.FailureReason.Length > FailureReasonMaxLength
            ? source.FailureReason[..FailureReasonMaxLength]
            : source.FailureReason;
        return queued;
    }

    internal static ContinuationQueuedIssue FromBranchDiverged(BranchDivergedIssue source, DateTimeOffset transitionedAt)
    {
        ContinuationQueuedIssue queued = new(source.Id, transitionedAt);
        queued.CopySharedPropertiesFrom(source);
        queued.BranchName = source.BranchName;
        string reason = source.DivergenceReason switch
        {
            BranchDivergenceReason.ForcePushProtected =>
                $"The previous run rebased {source.BranchName} onto the default branch, but {source.BranchName} is protected from force pushes, so the run's work was pushed to {source.SideBranchName} instead.",
            _ =>
                $"The previous run's work could not join commits pushed to {source.BranchName} during the run and was pushed to {source.SideBranchName} instead.",
        };
        queued.FailureReason = reason.Length > FailureReasonMaxLength ? reason[..FailureReasonMaxLength] : reason;
        return queued;
    }

    /// <summary>
    /// A Fresh claim found the issue's branch already carrying commits the default branch lacks, so the claim continues
    /// that work instead of re-implementing the issue. No prior run failed, so there is no failure reason.
    /// </summary>
    internal static ContinuationQueuedIssue FromFreshQueued(
        FreshQueuedIssue source,
        BranchNameValue branchName,
        DateTimeOffset transitionedAt)
    {
        ContinuationQueuedIssue queued = new(source.Id, transitionedAt);
        queued.CopySharedPropertiesFrom(source);
        queued.BranchName = branchName.Value;
        queued.FailureReason = string.Empty;
        return queued;
    }

    /// <summary>
    /// Triage released an issue whose open work is a branch without a pull request, with no open blockers: the work
    /// continues on <paramref name="branchName"/>, never a fresh run on a recomputed branch. No run failed, so there is
    /// no failure reason.
    /// </summary>
    internal static ContinuationQueuedIssue FromTriage(
        Issue source,
        TriageVerdict.Ready? verdict,
        string branchName,
        DateTimeOffset transitionedAt)
    {
        ContinuationQueuedIssue queued = new(source.Id, transitionedAt);
        queued.CopySharedPropertiesFrom(source);
        if (verdict is not null)
        {
            queued.RecordTriageVerdict(verdict);
        }

        queued.ReleaseOpenWork();
        queued.BranchName = branchName;
        queued.FailureReason = string.Empty;
        return queued;
    }

    internal static ContinuationQueuedIssue FromAwaitingInput(
        AwaitingInputIssue source,
        ResumeTarget.Continuation target,
        ClarificationInput input,
        DateTimeOffset answeredAt,
        DateTimeOffset transitionedAt)
    {
        ContinuationQueuedIssue queued = new(source.Id, transitionedAt);
        queued.CopySharedPropertiesFrom(source);
        queued.AnswerClarification(input, answeredAt);
        queued.ContinuedWithoutAnswer = input.IsContinuedWithoutAnswer;
        queued.BranchName = target.BranchName;
        queued.FailureReason = string.Empty;
        return queued;
    }

    internal static ContinuationQueuedIssue FromContinuationBlocked(
        ContinuationBlockedIssue source,
        DateTimeOffset transitionedAt)
    {
        ContinuationQueuedIssue queued = new(source.Id, transitionedAt);
        queued.CopySharedPropertiesFrom(source);
        queued.SetBlockedBy(source.BlockedBy);
        queued.BranchName = source.BranchName;
        queued.FailureReason = source.FailureReason;
        queued.ContinuedWithoutAnswer = source.ContinuedWithoutAnswer;
        return queued;
    }

    /// <summary>Open <paramref name="blockers"/> hold the continuation until they close: it starts no run meanwhile.</summary>
    public ContinuationBlockedIssue Block(IReadOnlyList<int> blockers, DateTimeOffset transitionedAt)
    {
        ContinuationBlockedIssue blocked = ContinuationBlockedIssue.FromContinuationQueued(this, blockers, transitionedAt);
        AddDomainEvent(new Events.IssueContinuationBlocked(Id, MonitoredRepositoryId));
        return blocked;
    }

    public override InProgressIssue Claim(WorkerRunId workerRunId, DateTimeOffset claimedAt)
    {
        InProgressIssue inProgress = InProgressIssue.FromContinuationQueued(this, workerRunId, claimedAt);
        AddDomainEvent(new Events.IssueInProgress(Id, MonitoredRepositoryId));
        return inProgress;
    }

    /// <summary>
    /// The issue's spec was rewritten since its last triage (ADR 0125): it goes back to triage holding its branch, so
    /// a ready verdict continues the work on it.
    /// </summary>
    public TriageQueuedIssue RetriageRewrittenSpec(DateTimeOffset transitionedAt)
    {
        TriageQueuedIssue queued = TriageQueuedIssue.HoldingOpenWork(this, OpenWork.OnBranch(BranchName), transitionedAt);
        AddDomainEvent(new Events.IssueTriageQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
