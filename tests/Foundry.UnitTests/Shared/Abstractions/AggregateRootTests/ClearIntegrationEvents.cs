using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Abstractions.AggregateRootTests;

public sealed class ClearIntegrationEvents
{
    [Fact]
    public void WhenEventsAddedThenCleared_IntegrationEventsIsEmpty()
    {
        // Arrange
        TestAggregateRoot sut = new(TestAggregateRootId.From(Guid.NewGuid()));
        sut.RaiseIntegrationEvent(new TestIntegrationEvent("First"));
        sut.RaiseIntegrationEvent(new TestIntegrationEvent("Second"));

        // Act
        sut.ClearIntegrationEvents();

        // Assert
        sut.IntegrationEvents.ShouldBeEmpty();
    }
}
