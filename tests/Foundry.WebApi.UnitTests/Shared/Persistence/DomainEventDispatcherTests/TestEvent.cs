using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.UnitTests.Shared.Persistence.DomainEventDispatcherTests;

public sealed record TestEvent(string Name) : IDomainEvent;

public sealed record SecondTestEvent(string Name) : IDomainEvent;

public sealed class TestEventHandler : IDomainEventHandler<TestEvent>
{
    public List<TestEvent> ReceivedEvents { get; } = [];

    public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken)
    {
        ReceivedEvents.Add(@event);
        return Task.CompletedTask;
    }
}

public sealed class SecondTestEventHandler : IDomainEventHandler<SecondTestEvent>
{
    public List<SecondTestEvent> ReceivedEvents { get; } = [];

    public Task HandleAsync(SecondTestEvent @event, CancellationToken cancellationToken)
    {
        ReceivedEvents.Add(@event);
        return Task.CompletedTask;
    }
}
