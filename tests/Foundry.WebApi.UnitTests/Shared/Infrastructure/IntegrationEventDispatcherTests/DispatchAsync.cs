using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Infrastructure.IntegrationEventDispatcherTests;

public sealed class DispatchAsync
{
    [Fact]
    public async Task WhenMultipleHandlersRegisteredForSameEventType_AllHandlersReceiveTheEvent()
    {
        // Arrange
        TestIntegrationEventHandler handlerA = new();
        TestIntegrationEventHandler handlerB = new();
        ServiceCollection services = new();
        services.AddSingleton<IIntegrationEventHandler<TestIntegrationEvent>>(handlerA);
        services.AddSingleton<IIntegrationEventHandler<TestIntegrationEvent>>(handlerB);
        IServiceProvider provider = services.BuildServiceProvider();

        IIntegrationEventDispatcher sut = new IntegrationEventDispatcher(provider);
        TestIntegrationEvent @event = new("SomethingHappened");

        // Act
        await sut.DispatchAsync([@event], CancellationToken.None);

        // Assert
        handlerA.ReceivedEvents.ShouldContain(@event);
        handlerB.ReceivedEvents.ShouldContain(@event);
    }

    [Fact]
    public async Task WhenOneHandlerRegistered_HandlerReceivesTheEvent()
    {
        // Arrange
        TestIntegrationEventHandler handler = new();
        ServiceCollection services = new();
        services.AddSingleton<IIntegrationEventHandler<TestIntegrationEvent>>(handler);
        IServiceProvider provider = services.BuildServiceProvider();

        IIntegrationEventDispatcher sut = new IntegrationEventDispatcher(provider);
        TestIntegrationEvent @event = new("SomethingHappened");

        // Act
        await sut.DispatchAsync([@event], CancellationToken.None);

        // Assert
        handler.ReceivedEvents.ShouldContain(@event);
    }

    [Fact]
    public async Task WhenNoHandlersRegistered_DoesNotThrow()
    {
        // Arrange
        ServiceCollection services = new();
        IServiceProvider provider = services.BuildServiceProvider();

        IIntegrationEventDispatcher sut = new IntegrationEventDispatcher(provider);
        TestIntegrationEvent @event = new("SomethingHappened");

        // Act
        Task act = sut.DispatchAsync([@event], CancellationToken.None);

        // Assert
        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task WhenMultipleEventsDispatched_HandlersReceiveEventsInOrder()
    {
        // Arrange
        TestIntegrationEventHandler firstHandler = new();
        SecondTestIntegrationEventHandler secondHandler = new();

        ServiceCollection services = new();
        services.AddSingleton<IIntegrationEventHandler<TestIntegrationEvent>>(firstHandler);
        services.AddSingleton<IIntegrationEventHandler<SecondTestIntegrationEvent>>(secondHandler);
        IServiceProvider provider = services.BuildServiceProvider();

        IIntegrationEventDispatcher sut = new IntegrationEventDispatcher(provider);
        TestIntegrationEvent firstEvent = new("First");
        SecondTestIntegrationEvent secondEvent = new("Second");

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
