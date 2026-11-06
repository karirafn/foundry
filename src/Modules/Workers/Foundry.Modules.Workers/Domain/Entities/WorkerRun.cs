using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.Entities;

public abstract class WorkerRun : AggregateRoot<WorkerRunId>, IStateMachine<WorkerRun>
{
    // Private parameterless constructor for EF Core materialization.
    protected WorkerRun() : base(WorkerRunId.New())
    {
    }

    protected WorkerRun(WorkerRunId id, IssueId issueId, DateTimeOffset createdAt, WorkerRole role, string? image) : base(id)
    {
        IssueId = issueId;
        CreatedAt = createdAt;
        Role = role;
        Image = image;
    }

    public IssueId IssueId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The role the run was dispatched with (ADR 0095).</summary>
    public WorkerRole Role { get; private set; } = WorkerRole.Implement;

    /// <summary>
    /// The worker image the run was dispatched on: the tag, pinned to the image ID when known
    /// (<c>tag@sha256:...</c>). <see langword="null"/> for runs recorded before images were.
    /// </summary>
    public string? Image { get; private set; }
}
