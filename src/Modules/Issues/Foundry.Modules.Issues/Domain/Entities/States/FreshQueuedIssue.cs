using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class FreshQueuedIssue : QueuedIssue
{
    // Private parameterless constructor for EF Core materialization.
    private FreshQueuedIssue()
    {
    }

    private FreshQueuedIssue(IssueId id) : base(id)
    {
    }

    public override int TierRank => 2;

    public override BranchName DispatchBranchName =>
        BranchName.Generate(KindClassification, IssueNumber, Title);

    public override DispatchContext Context =>
        new DispatchContext.Fresh(DispatchBranchName.Value);

    internal static FreshQueuedIssue FromDetected(DetectedIssue detected)
    {
        FreshQueuedIssue queued = new(detected.Id);
        queued.CopySharedPropertiesFrom(detected);
        return queued;
    }

    internal static FreshQueuedIssue FromBlocked(BlockedIssue blocked)
    {
        FreshQueuedIssue queued = new(blocked.Id);
        queued.CopySharedPropertiesFrom(blocked);
        return queued;
    }

    internal static FreshQueuedIssue FromRetry(Issue source)
    {
        FreshQueuedIssue queued = new(source.Id);
        queued.CopySharedPropertiesFrom(source);
        return queued;
    }

    internal static FreshQueuedIssue FromAwaitingInput(AwaitingInputIssue source, string input, DateTimeOffset answeredAt)
    {
        FreshQueuedIssue queued = new(source.Id);
        queued.CopySharedPropertiesFrom(source);
        queued.AnswerClarification(input, answeredAt);
        return queued;
    }

    public BlockedIssue Block(IReadOnlyList<int> blockers)
    {
        if (blockers.Count == 0)
        {
            throw new InvalidOperationException("Cannot block an issue without specifying at least one blocker.");
        }

        BlockedIssue blocked = BlockedIssue.FromQueued(this, blockers);
        AddDomainEvent(new Events.IssueBlocked(Id, MonitoredRepositoryId));
        return blocked;
    }

    public override InProgressIssue Claim(WorkerRunId workerRunId)
    {
        InProgressIssue inProgress = InProgressIssue.FromQueued(this, workerRunId);
        AddDomainEvent(new Events.IssueInProgress(Id, MonitoredRepositoryId));
        return inProgress;
    }
}
