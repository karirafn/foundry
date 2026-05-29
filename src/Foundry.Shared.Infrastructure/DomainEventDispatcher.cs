using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Shared.Infrastructure;

public sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
    {
        foreach (IDomainEvent @event in events)
        {
            Type handlerType = typeof(IDomainEventHandler<>).MakeGenericType(@event.GetType());
            IEnumerable<object?> handlers = services.GetServices(handlerType);
            foreach (object? handler in handlers)
            {
                if (handler is not IDomainEventHandler typedHandler)
                {
                    continue;
                }

                await typedHandler.HandleAsync(@event, cancellationToken);
            }
        }
    }
}
