namespace Foundry.Shared;

public interface IIntegrationEventProcessor
{
    Task ProcessAsync(IIntegrationEvent @event, CancellationToken cancellationToken);
}
