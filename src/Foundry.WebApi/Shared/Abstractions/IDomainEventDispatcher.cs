namespace Foundry.WebApi.Shared.Abstractions;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken);
}
