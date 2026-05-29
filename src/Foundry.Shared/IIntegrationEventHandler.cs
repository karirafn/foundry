namespace Foundry.Shared;

// IIntegrationEventHandler is the standard DDD term — renaming it would break ubiquitous language
#pragma warning disable CA1711
public interface IIntegrationEventHandler
{
    Task HandleAsync(IIntegrationEvent @event, CancellationToken cancellationToken);
}

public interface IIntegrationEventHandler<TEvent> : IIntegrationEventHandler
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);

    Task IIntegrationEventHandler.HandleAsync(IIntegrationEvent @event, CancellationToken cancellationToken)
    {
        return HandleAsync((TEvent)@event, cancellationToken);
    }
}
#pragma warning restore CA1711
