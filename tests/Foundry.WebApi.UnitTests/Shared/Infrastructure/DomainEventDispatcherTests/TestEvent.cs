using Foundry.Shared;

namespace Foundry.WebApi.UnitTests.Shared.Infrastructure.DomainEventDispatcherTests;

internal sealed record TestEvent(string Name) : IDomainEvent;

internal sealed record SecondTestEvent(string Name) : IDomainEvent;

internal sealed class TestEventHandler : IDomainEventHandler<TestEvent>
{
    public List<TestEvent> ReceivedEvents { get; } = [];

    public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken)
    {
        ReceivedEvents.Add(@event);
        return Task.CompletedTask;
    }
}

internal sealed class SecondTestEventHandler : IDomainEventHandler<SecondTestEvent>
{
    public List<SecondTestEvent> ReceivedEvents { get; } = [];

    public Task HandleAsync(SecondTestEvent @event, CancellationToken cancellationToken)
    {
        ReceivedEvents.Add(@event);
        return Task.CompletedTask;
    }
}
