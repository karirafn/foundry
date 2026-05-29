using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.AggregateRootTests;

public sealed class IndependentEventCollections
{
    [Fact]
    public void WhenDomainEventAdded_IntegrationEventsStaysEmpty()
    {
        // Arrange
        TestAggregateRoot sut = new(TestAggregateRootId.From(Guid.NewGuid()));

        // Act
        sut.RaiseEvent(new TestDomainEvent("SomethingHappened"));

        // Assert
        sut.IntegrationEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenIntegrationEventAdded_DomainEventsStaysEmpty()
    {
        // Arrange
        TestAggregateRoot sut = new(TestAggregateRootId.From(Guid.NewGuid()));

        // Act
        sut.RaiseIntegrationEvent(new TestIntegrationEvent("SomethingPublished"));

        // Assert
        sut.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenDomainEventsCleared_IntegrationEventsUnaffected()
    {
        // Arrange
        TestAggregateRoot sut = new(TestAggregateRootId.From(Guid.NewGuid()));
        sut.RaiseEvent(new TestDomainEvent("SomethingHappened"));
        sut.RaiseIntegrationEvent(new TestIntegrationEvent("SomethingPublished"));

        // Act
        sut.ClearDomainEvents();

        // Assert
        sut.IntegrationEvents.Count.ShouldBe(1);
    }

    [Fact]
    public void WhenIntegrationEventsCleared_DomainEventsUnaffected()
    {
        // Arrange
        TestAggregateRoot sut = new(TestAggregateRootId.From(Guid.NewGuid()));
        sut.RaiseEvent(new TestDomainEvent("SomethingHappened"));
        sut.RaiseIntegrationEvent(new TestIntegrationEvent("SomethingPublished"));

        // Act
        sut.ClearIntegrationEvents();

        // Assert
        sut.DomainEvents.Count.ShouldBe(1);
    }
}
