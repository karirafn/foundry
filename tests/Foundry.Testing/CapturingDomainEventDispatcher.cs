using Foundry.Shared;

namespace Foundry.Testing;

public sealed class CapturingDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly List<IDomainEvent> _events = [];

    public IReadOnlyList<IDomainEvent> DispatchedEvents => _events;

    public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
    {
        _events.AddRange(events);
        return Task.CompletedTask;
    }
}
