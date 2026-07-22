using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Shared.Infrastructure.Outbox;

public sealed class IntegrationEventProcessor(IServiceProvider services) : IIntegrationEventProcessor
{
    public async Task ProcessAsync(IIntegrationEvent @event, CancellationToken cancellationToken)
    {
        Type handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(@event.GetType());
        IEnumerable<object?> handlers = services.GetServices(handlerType);
        foreach (object? handler in handlers)
        {
            if (handler is not IIntegrationEventHandler typedHandler)
            {
                continue;
            }

            await typedHandler.HandleAsync(@event, cancellationToken);
        }
    }
}
