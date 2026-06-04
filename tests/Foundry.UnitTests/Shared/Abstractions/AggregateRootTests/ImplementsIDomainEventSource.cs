using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Abstractions.AggregateRootTests;

public sealed class ImplementsIDomainEventSource
{
    [Fact]
    public void AggregateRoot_ImplementsIDomainEventSource()
    {
        // Arrange
        TestAggregateRoot sut = new(TestAggregateRootId.From(Guid.NewGuid()));

        // Assert
        sut.ShouldBeAssignableTo<IDomainEventSource>();
    }

    [Fact]
    public void WhenCastToIDomainEventSource_DomainEventsAreAccessible()
    {
        // Arrange
        TestAggregateRoot sut = new(TestAggregateRootId.From(Guid.NewGuid()));
        TestDomainEvent domainEvent = new("SomethingHappened");
        sut.RaiseEvent(domainEvent);

        // Act
        IDomainEventSource source = sut;

        // Assert
        source.DomainEvents.ShouldContain(domainEvent);
    }

    [Fact]
    public void WhenCastToIDomainEventSource_ClearDomainEventsWorks()
    {
        // Arrange
        TestAggregateRoot sut = new(TestAggregateRootId.From(Guid.NewGuid()));
        sut.RaiseEvent(new TestDomainEvent("SomethingHappened"));
        IDomainEventSource source = sut;

        // Act
        source.ClearDomainEvents();

        // Assert
        sut.DomainEvents.ShouldBeEmpty();
    }
}
