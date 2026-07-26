using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Shared.Infrastructure.Outbox;

public sealed class IntegrationEventProcessor(IServiceProvider services, DbContext dbContext) : IIntegrationEventProcessor
{
    public async Task ProcessAsync(Guid eventId, IIntegrationEvent @event, CancellationToken cancellationToken)
    {
        Type handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(@event.GetType());
        IEnumerable<object?> handlers = services.GetServices(handlerType);

        foreach (object? handler in handlers)
        {
            if (handler is not IIntegrationEventHandler typedHandler)
            {
                continue;
            }

            string handlerName = handler.GetType().FullName!;

            bool alreadyProcessed = await dbContext.IsProcessedAsync(eventId, handlerName, cancellationToken);

            if (alreadyProcessed)
            {
                continue;
            }

            // Invoke first, then durably record success before moving to the next handler.
            // A throw here propagates to the caller so the relay records failure and retries the
            // whole message — but any handlers that already committed their processed_events row
            // will be skipped on the next attempt (partial fan-out safety).
            await typedHandler.HandleAsync(@event, cancellationToken);

            try
            {
                dbContext.Set<ProcessedEvent>().Add(ProcessedEvent.For(eventId, handlerName, DateTimeOffset.UtcNow));
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // A duplicate-key violation means another instance committed the same row
                // concurrently (at-least-once race). Treat as already-processed and continue.
                dbContext.ChangeTracker.Clear();
            }
        }
    }
}
