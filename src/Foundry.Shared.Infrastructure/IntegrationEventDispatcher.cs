using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Shared.Infrastructure;

public sealed class IntegrationEventDispatcher(IServiceProvider services) : IIntegrationEventDispatcher
{
    public async Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
    {
        foreach (IIntegrationEvent @event in events)
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
}
