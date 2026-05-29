using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Abstractions.AggregateRootTests;

public sealed class AddIntegrationEvent
{
    [Fact]
    public void WhenNewAggregate_IntegrationEventsIsEmpty()
    {
        // Arrange
        TestAggregateRoot sut = new(TestAggregateRootId.From(Guid.NewGuid()));

        // Assert
        sut.IntegrationEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenOneEventAdded_IntegrationEventsContainsThatEvent()
    {
        // Arrange
        TestAggregateRoot sut = new(TestAggregateRootId.From(Guid.NewGuid()));
        TestIntegrationEvent integrationEvent = new("SomethingPublished");

        // Act
        sut.RaiseIntegrationEvent(integrationEvent);

        // Assert
        sut.IntegrationEvents.ShouldContain(integrationEvent);
    }

    [Fact]
    public void WhenMultipleEventsAdded_IntegrationEventsAccumulatesAll()
    {
        // Arrange
        TestAggregateRoot sut = new(TestAggregateRootId.From(Guid.NewGuid()));
        TestIntegrationEvent first = new("First");
        TestIntegrationEvent second = new("Second");
        TestIntegrationEvent third = new("Third");

        // Act
        sut.RaiseIntegrationEvent(first);
        sut.RaiseIntegrationEvent(second);
        sut.RaiseIntegrationEvent(third);

        // Assert
        sut.IntegrationEvents.ShouldSatisfyAllConditions(
            () => sut.IntegrationEvents.Count.ShouldBe(3),
            () => sut.IntegrationEvents.ShouldContain(first),
            () => sut.IntegrationEvents.ShouldContain(second),
            () => sut.IntegrationEvents.ShouldContain(third)
        );
    }
}
