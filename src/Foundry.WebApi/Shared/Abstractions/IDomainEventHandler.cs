namespace Foundry.WebApi.Shared.Abstractions;

// IDomainEventHandler is the standard DDD term — renaming it would break ubiquitous language
#pragma warning disable CA1711
public interface IDomainEventHandler<TEvent>
#pragma warning restore CA1711
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
