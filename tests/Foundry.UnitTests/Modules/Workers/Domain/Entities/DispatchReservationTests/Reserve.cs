using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.DispatchReservationTests;

public sealed class Reserve
{
    [Fact]
    public void WhenReserved_IdEqualsWorkerRunId()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        DateTimeOffset reservedAt = DateTimeOffset.UtcNow;

        // Act
        DispatchReservation reservation = DispatchReservation.Reserve(workerRunId, reservedAt);

        // Assert
        reservation.Id.ShouldBe(workerRunId);
    }

    [Fact]
    public void WhenReserved_ReservedAtEqualsSuppliedInstant()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        DateTimeOffset reservedAt = new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

        // Act
        DispatchReservation reservation = DispatchReservation.Reserve(workerRunId, reservedAt);

        // Assert
        reservation.ReservedAt.ShouldBe(reservedAt);
    }
}
