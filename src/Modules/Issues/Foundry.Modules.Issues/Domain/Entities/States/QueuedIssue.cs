using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class QueuedIssue : ClaimableIssue
{
    // Private parameterless constructor for EF Core materialization.
    private QueuedIssue()
    {
    }

    private QueuedIssue(IssueId id) : base(id)
    {
    }

    public override int TierRank => 2;

    public override BranchName DispatchBranchName =>
        BranchName.Generate(IssueKind.BranchPrefix, IssueNumber, Title);

    public override DispatchContext Context =>
        new DispatchContext.Fresh(DispatchBranchName.Value);

    internal static QueuedIssue FromDetected(DetectedIssue detected)
    {
        QueuedIssue queued = new(detected.Id);
        queued.SetSharedProperties(
            detected.MonitoredRepositoryId,
            detected.IssueNumber,
            detected.Title,
            detected.Body,
            detected.Author,
            detected.Url,
            detected.Labels,
            detected.DetectedAt);
        return queued;
    }

    internal static QueuedIssue FromBlocked(BlockedIssue blocked)
    {
        QueuedIssue queued = new(blocked.Id);
        queued.SetSharedProperties(
            blocked.MonitoredRepositoryId,
            blocked.IssueNumber,
            blocked.Title,
            blocked.Body,
            blocked.Author,
            blocked.Url,
            blocked.Labels,
            blocked.DetectedAt);
        return queued;
    }

    internal static QueuedIssue FromRetry(Issue source)
    {
        QueuedIssue queued = new(source.Id);
        queued.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Body,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
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

    public override InProgressIssue Claim(Guid workerRunId)
    {
        InProgressIssue inProgress = InProgressIssue.FromQueued(this, workerRunId);
        AddDomainEvent(new Events.IssueInProgress(Id, MonitoredRepositoryId));
        return inProgress;
    }
}
