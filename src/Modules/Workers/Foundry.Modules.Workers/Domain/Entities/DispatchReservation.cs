using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.Entities;

public sealed class DispatchReservation : AggregateRoot<WorkerRunId>
{
    // Private parameterless constructor for EF Core materialization.
    private DispatchReservation() : base(WorkerRunId.New())
    {
    }

    private DispatchReservation(WorkerRunId id, DateTimeOffset reservedAt) : base(id)
    {
        ReservedAt = reservedAt;
    }

    public DateTimeOffset ReservedAt { get; private set; }

    public static DispatchReservation Reserve(WorkerRunId workerRunId, DateTimeOffset reservedAt)
    {
        return new DispatchReservation(workerRunId, reservedAt);
    }
}
