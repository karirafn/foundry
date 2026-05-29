namespace Foundry.Shared;

// IDomainEventHandler is the standard DDD term — renaming it would break ubiquitous language
#pragma warning disable CA1711
public interface IDomainEventHandler
{
    Task HandleAsync(IDomainEvent @event, CancellationToken cancellationToken);
}

public interface IDomainEventHandler<TEvent> : IDomainEventHandler
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);

    Task IDomainEventHandler.HandleAsync(IDomainEvent @event, CancellationToken cancellationToken)
    {
        return HandleAsync((TEvent)@event, cancellationToken);
    }
}
#pragma warning restore CA1711
