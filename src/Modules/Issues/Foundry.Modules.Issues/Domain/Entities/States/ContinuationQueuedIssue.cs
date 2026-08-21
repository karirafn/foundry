using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Shared;

using BranchNameValue = Foundry.Shared.BranchName;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class ContinuationQueuedIssue : QueuedIssue
{
    public const int FailureReasonMaxLength = 500;

    // Private parameterless constructor for EF Core materialization.
    private ContinuationQueuedIssue()
    {
    }

    private ContinuationQueuedIssue(IssueId id) : base(id)
    {
    }

    public override int TierRank => 1;

    public override BranchNameValue DispatchBranchName => BranchNameValue.From(BranchName);

    public override DispatchContext Context =>
        new DispatchContext.Continuation(BranchName, FailureReason);

    public string BranchName { get; private set; } = string.Empty;

    public string FailureReason { get; private set; } = string.Empty;

    internal static ContinuationQueuedIssue FromContinuableFailed(ContinuableFailedIssue source)
    {
        ContinuationQueuedIssue queued = new(source.Id);
        queued.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        queued.BranchName = source.BranchName;
        queued.FailureReason = source.FailureReason.Length > FailureReasonMaxLength
            ? source.FailureReason[..FailureReasonMaxLength]
            : source.FailureReason;
        return queued;
    }

    public override InProgressIssue Claim(Guid workerRunId)
    {
        InProgressIssue inProgress = InProgressIssue.FromContinuationQueued(this, workerRunId);
        AddDomainEvent(new Events.IssueInProgress(Id, MonitoredRepositoryId));
        return inProgress;
    }
}
