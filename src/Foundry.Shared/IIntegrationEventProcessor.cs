namespace Foundry.Shared;

public interface IIntegrationEventProcessor
{
    Task ProcessAsync(Guid eventId, IIntegrationEvent @event, CancellationToken cancellationToken);
}
