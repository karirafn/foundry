using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;

namespace Foundry.Testing;

public sealed class DispatchReservationBuilder
{
    private WorkerRunId _workerRunId = WorkerRunId.New();
    private DateTimeOffset _reservedAt = DateTimeOffset.UtcNow;

    public DispatchReservationBuilder WithWorkerRunId(WorkerRunId workerRunId)
    {
        _workerRunId = workerRunId;
        return this;
    }

    public DispatchReservationBuilder WithReservedAt(DateTimeOffset reservedAt)
    {
        _reservedAt = reservedAt;
        return this;
    }

    public DispatchReservation Build()
    {
        return DispatchReservation.Reserve(_workerRunId, _reservedAt);
    }
}
