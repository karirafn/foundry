using Foundry.Shared;

namespace Foundry.Shared.Infrastructure.Outbox;

public sealed class OutboxIntegrationEventDispatcher(IntegrationEventCollector collector) : IIntegrationEventDispatcher
{
    public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
    {
        foreach (IIntegrationEvent @event in events)
        {
            collector.Enqueue(@event);
        }

        return Task.CompletedTask;
    }
}
