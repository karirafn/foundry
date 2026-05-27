using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.AggregateRootTests;

public sealed class AddDomainEvent
{
    [Fact]
    public void WhenNewAggregate_DomainEventsIsEmpty()
    {
        // Arrange
        TestAggregateRoot sut = new(TestAggregateRootId.From(Guid.NewGuid()));

        // Assert
        sut.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenOneEventAdded_DomainEventsContainsThatEvent()
    {
        // Arrange
        TestAggregateRoot sut = new(TestAggregateRootId.From(Guid.NewGuid()));
        TestDomainEvent domainEvent = new("SomethingHappened");

        // Act
        sut.RaiseEvent(domainEvent);

        // Assert
        sut.DomainEvents.ShouldContain(domainEvent);
    }

    [Fact]
    public void WhenMultipleEventsAdded_DomainEventsAccumulatesAll()
    {
        // Arrange
        TestAggregateRoot sut = new(TestAggregateRootId.From(Guid.NewGuid()));
        TestDomainEvent first = new("First");
        TestDomainEvent second = new("Second");
        TestDomainEvent third = new("Third");

        // Act
        sut.RaiseEvent(first);
        sut.RaiseEvent(second);
        sut.RaiseEvent(third);

        // Assert
        sut.DomainEvents.ShouldSatisfyAllConditions(
            () => sut.DomainEvents.Count.ShouldBe(3),
            () => sut.DomainEvents.ShouldContain(first),
            () => sut.DomainEvents.ShouldContain(second),
            () => sut.DomainEvents.ShouldContain(third)
        );
    }
}
