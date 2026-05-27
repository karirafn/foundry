using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.AggregateRootTests;

public sealed class ClearDomainEvents
{
    [Fact]
    public void WhenEventsAddedThenCleared_DomainEventsIsEmpty()
    {
        // Arrange
        TestAggregateRoot sut = new(TestAggregateRootId.From(Guid.NewGuid()));
        sut.RaiseEvent(new TestDomainEvent("First"));
        sut.RaiseEvent(new TestDomainEvent("Second"));

        // Act
        sut.ClearDomainEvents();

        // Assert
        sut.DomainEvents.ShouldBeEmpty();
    }
}
