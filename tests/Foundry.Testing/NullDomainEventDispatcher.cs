using Foundry.Shared;

namespace Foundry.Testing;

public sealed class NullDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
