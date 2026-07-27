using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Events;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.WorkerCapacityAvailableTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        Guid workerRunId = Guid.NewGuid();

        // Act
        WorkerCapacityAvailable @event = new(workerRunId);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.WorkerRunId.ShouldBe(workerRunId);
    }
}
