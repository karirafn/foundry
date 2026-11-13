using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class UnchangedIssue : Issue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => true;

    public override bool AwaitsDispatch => false;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private UnchangedIssue()
    {
    }

    private UnchangedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    internal static UnchangedIssue FromInProgress(InProgressIssue source, DateTimeOffset transitionedAt)
    {
        UnchangedIssue unchanged = new(source.Id, transitionedAt);
        unchanged.CopySharedPropertiesFrom(source);
        unchanged.WorkerRunId = source.WorkerRunId;
        return unchanged;
    }

    public FreshQueuedIssue Retry(DateTimeOffset transitionedAt)
    {
        FreshQueuedIssue queued = FreshQueuedIssue.FromRetry(this, transitionedAt);
        AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
