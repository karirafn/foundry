using Foundry.Shared;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.OutboxRelayServiceTests;

internal sealed record TestRelayEvent(string Name) : IIntegrationEvent;

internal sealed class RecordingRelayEventHandler : IIntegrationEventHandler<TestRelayEvent>
{
    public List<TestRelayEvent> ReceivedEvents { get; } = [];

    public Task HandleAsync(TestRelayEvent @event, CancellationToken cancellationToken)
    {
        ReceivedEvents.Add(@event);
        return Task.CompletedTask;
    }
}
