using Foundry.Modules.Issues.Contracts;
using Foundry.Shared;
using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Domain.Entities;

public abstract class WorkerRun : AggregateRoot<WorkerRunId>, IStateMachine<WorkerRun>
{
    // Private parameterless constructor for EF Core materialization.
    protected WorkerRun() : base(WorkerRunId.New())
    {
    }

    protected WorkerRun(WorkerRunId id, IssueId issueId, DateTimeOffset createdAt) : base(id)
    {
        IssueId = issueId;
        CreatedAt = createdAt;
    }

    public IssueId IssueId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
