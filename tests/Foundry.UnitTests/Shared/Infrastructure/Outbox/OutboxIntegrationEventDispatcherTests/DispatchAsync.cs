using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.OutboxIntegrationEventDispatcherTests;

public sealed class DispatchAsync
{
    [Fact]
    public async Task WhenEventDispatched_EventIsEnqueuedInCollector()
    {
        // Arrange
        IntegrationEventCollector collector = new();
        OutboxIntegrationEventDispatcher sut = new(collector);
        TestOutboxIntegrationEvent @event = new("test-value");

        // Act
        await sut.DispatchAsync([@event], CancellationToken.None);

        // Assert
        collector.PendingCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenMultipleEventsDispatched_AllEventsAreEnqueuedInCollector()
    {
        // Arrange
        IntegrationEventCollector collector = new();
        OutboxIntegrationEventDispatcher sut = new(collector);

        // Act
        await sut.DispatchAsync(
            [new TestOutboxIntegrationEvent("first"), new TestOutboxIntegrationEvent("second")],
            CancellationToken.None);

        // Assert
        collector.PendingCount.ShouldBe(2);
    }

    [Fact]
    public async Task WhenNoEventsDispatched_CollectorRemainsEmpty()
    {
        // Arrange
        IntegrationEventCollector collector = new();
        OutboxIntegrationEventDispatcher sut = new(collector);

        // Act
        await sut.DispatchAsync([], CancellationToken.None);

        // Assert
        collector.HasPending.ShouldBeFalse();
    }
}
