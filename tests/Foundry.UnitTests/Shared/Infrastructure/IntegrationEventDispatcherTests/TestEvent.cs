using Foundry.Shared;

namespace Foundry.UnitTests.Shared.Infrastructure.IntegrationEventDispatcherTests;

internal sealed record TestIntegrationEvent(string Name) : IIntegrationEvent;

internal sealed record SecondTestIntegrationEvent(string Name) : IIntegrationEvent;

internal sealed class TestIntegrationEventHandler : IIntegrationEventHandler<TestIntegrationEvent>
{
    public List<TestIntegrationEvent> ReceivedEvents { get; } = [];

    public Task HandleAsync(TestIntegrationEvent @event, CancellationToken cancellationToken)
    {
        ReceivedEvents.Add(@event);
        return Task.CompletedTask;
    }
}

internal sealed class SecondTestIntegrationEventHandler : IIntegrationEventHandler<SecondTestIntegrationEvent>
{
    public List<SecondTestIntegrationEvent> ReceivedEvents { get; } = [];

    public Task HandleAsync(SecondTestIntegrationEvent @event, CancellationToken cancellationToken)
    {
        ReceivedEvents.Add(@event);
        return Task.CompletedTask;
    }
}
