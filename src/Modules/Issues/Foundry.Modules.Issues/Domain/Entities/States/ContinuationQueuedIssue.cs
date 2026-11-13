using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using BranchNameValue = Foundry.Shared.BranchName;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class ContinuationQueuedIssue : QueuedIssue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => true;

    public override IssueCodeStep? CodeStep => null;

    public const int FailureReasonMaxLength = 500;

    // Private parameterless constructor for EF Core materialization.
    private ContinuationQueuedIssue()
    {
    }

    private ContinuationQueuedIssue(IssueId id) : base(id)
    {
    }

    public override int TierRank => 1;

    public override BranchNameValue? DispatchBranchName => BranchNameValue.From(BranchName);

    public override DispatchContext Context =>
        new DispatchContext.Continuation(BranchName, FailureReason);

    public string BranchName { get; private set; } = string.Empty;

    public string FailureReason { get; private set; } = string.Empty;

    internal static ContinuationQueuedIssue FromContinuableFailed(ContinuableFailedIssue source)
    {
        ContinuationQueuedIssue queued = new(source.Id);
        queued.CopySharedPropertiesFrom(source);
        queued.BranchName = source.BranchName;
        queued.FailureReason = source.FailureReason.Length > FailureReasonMaxLength
            ? source.FailureReason[..FailureReasonMaxLength]
            : source.FailureReason;
        return queued;
    }

    internal static ContinuationQueuedIssue FromBranchDiverged(BranchDivergedIssue source)
    {
        ContinuationQueuedIssue queued = new(source.Id);
        queued.CopySharedPropertiesFrom(source);
        queued.BranchName = source.BranchName;
        string reason =
            $"The previous run's work conflicted with commits pushed to {source.BranchName} during the run and was pushed to {source.SideBranchName} instead.";
        queued.FailureReason = reason.Length > FailureReasonMaxLength ? reason[..FailureReasonMaxLength] : reason;
        return queued;
    }

    internal static ContinuationQueuedIssue FromAwaitingInput(
        AwaitingInputIssue source,
        ResumeTarget.Continuation target,
        string input,
        DateTimeOffset answeredAt)
    {
        ContinuationQueuedIssue queued = new(source.Id);
        queued.CopySharedPropertiesFrom(source);
        queued.AnswerClarification(input, answeredAt);
        queued.BranchName = target.BranchName;
        queued.FailureReason = string.Empty;
        return queued;
    }

    public override InProgressIssue Claim(WorkerRunId workerRunId)
    {
        InProgressIssue inProgress = InProgressIssue.FromContinuationQueued(this, workerRunId);
        AddDomainEvent(new Events.IssueInProgress(Id, MonitoredRepositoryId));
        return inProgress;
    }
}
