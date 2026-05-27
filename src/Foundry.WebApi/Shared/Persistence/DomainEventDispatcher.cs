using Foundry.WebApi.Shared.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Foundry.WebApi.Shared.Persistence;

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
                await ((dynamic)handler!).HandleAsync((dynamic)@event, cancellationToken);
            }
        }
    }
}
