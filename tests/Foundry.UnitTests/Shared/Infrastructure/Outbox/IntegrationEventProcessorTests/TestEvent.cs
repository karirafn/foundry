using Foundry.Shared;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.IntegrationEventProcessorTests;

internal sealed record TestProcessorEvent(string Name) : IIntegrationEvent;

internal sealed class RecordingProcessorEventHandler : IIntegrationEventHandler<TestProcessorEvent>
{
    public List<TestProcessorEvent> ReceivedEvents { get; } = [];

    public Task HandleAsync(TestProcessorEvent @event, CancellationToken cancellationToken)
    {
        ReceivedEvents.Add(@event);
        return Task.CompletedTask;
    }
}
