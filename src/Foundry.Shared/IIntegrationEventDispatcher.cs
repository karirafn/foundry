namespace Foundry.Shared;

public interface IIntegrationEventDispatcher
{
    Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken);
}
