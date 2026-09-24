using Foundry.Shared;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.IntegrationEventProcessorTests;

internal sealed record TestProcessorEvent(string Name) : IIntegrationEvent;

[IntegrationEventHandlerIdentity("Test.RecordingProcessor")]
internal sealed class RecordingProcessorEventHandler : IIntegrationEventHandler<TestProcessorEvent>
{
    public List<TestProcessorEvent> ReceivedEvents { get; } = [];

    public Task HandleAsync(TestProcessorEvent @event, CancellationToken cancellationToken)
    {
        ReceivedEvents.Add(@event);
        return Task.CompletedTask;
    }
}

[IntegrationEventHandlerIdentity("Test.SecondRecordingProcessor")]
internal sealed class SecondRecordingProcessorEventHandler : IIntegrationEventHandler<TestProcessorEvent>
{
    public List<TestProcessorEvent> ReceivedEvents { get; } = [];

    public Task HandleAsync(TestProcessorEvent @event, CancellationToken cancellationToken)
    {
        ReceivedEvents.Add(@event);
        return Task.CompletedTask;
    }
}
