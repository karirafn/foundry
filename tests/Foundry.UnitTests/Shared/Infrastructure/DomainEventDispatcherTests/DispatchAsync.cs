using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.DomainEventDispatcherTests;

public sealed class DispatchAsync
{
    [Fact]
    public async Task WhenOneHandlerRegistered_HandlerReceivesTheEvent()
    {
        // Arrange
        TestEventHandler handler = new();
        ServiceCollection services = new();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(handler);
        IServiceProvider provider = services.BuildServiceProvider();

        IDomainEventDispatcher sut = new DomainEventDispatcher(provider);
        TestEvent @event = new("SomethingHappened");

        // Act
        await sut.DispatchAsync([@event], CancellationToken.None);

        // Assert
        handler.ReceivedEvents.ShouldContain(@event);
    }

    [Fact]
    public async Task WhenMultipleHandlersRegisteredForSameEventType_AllHandlersReceiveTheEvent()
    {
        // Arrange
        TestEventHandler handlerA = new();
        TestEventHandler handlerB = new();
        ServiceCollection services = new();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(handlerA);
        services.AddSingleton<IDomainEventHandler<TestEvent>>(handlerB);
        IServiceProvider provider = services.BuildServiceProvider();

        IDomainEventDispatcher sut = new DomainEventDispatcher(provider);
        TestEvent @event = new("SomethingHappened");

        // Act
        await sut.DispatchAsync([@event], CancellationToken.None);

        // Assert
        handlerA.ReceivedEvents.ShouldContain(@event);
        handlerB.ReceivedEvents.ShouldContain(@event);
    }

    [Fact]
    public async Task WhenNoHandlersRegistered_DoesNotThrow()
    {
        // Arrange
        ServiceCollection services = new();
        IServiceProvider provider = services.BuildServiceProvider();

        IDomainEventDispatcher sut = new DomainEventDispatcher(provider);
        TestEvent @event = new("SomethingHappened");

        // Act
        Task act = sut.DispatchAsync([@event], CancellationToken.None);

        // Assert
        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task WhenMultipleEventsDispatched_HandlersReceiveEventsInOrder()
    {
        // Arrange
        TestEventHandler firstHandler = new();
        SecondTestEventHandler secondHandler = new();

        ServiceCollection services = new();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(firstHandler);
        services.AddSingleton<IDomainEventHandler<SecondTestEvent>>(secondHandler);
        IServiceProvider provider = services.BuildServiceProvider();

        IDomainEventDispatcher sut = new DomainEventDispatcher(provider);
        TestEvent firstEvent = new("First");
        SecondTestEvent secondEvent = new("Second");

        // Act
        await sut.DispatchAsync([firstEvent, secondEvent], CancellationToken.None);

        // Assert
        firstHandler.ReceivedEvents.ShouldSatisfyAllConditions(
            () => firstHandler.ReceivedEvents.Count.ShouldBe(1),
            () => firstHandler.ReceivedEvents[0].ShouldBe(firstEvent)
        );
        secondHandler.ReceivedEvents.ShouldSatisfyAllConditions(
            () => secondHandler.ReceivedEvents.Count.ShouldBe(1),
            () => secondHandler.ReceivedEvents[0].ShouldBe(secondEvent)
        );
    }
}
