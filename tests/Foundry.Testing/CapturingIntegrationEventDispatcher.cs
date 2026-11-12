using Foundry.Shared;

namespace Foundry.Testing;

public sealed class CapturingIntegrationEventDispatcher : IIntegrationEventDispatcher
{
    private readonly List<IIntegrationEvent> _events = [];

    public IReadOnlyList<IIntegrationEvent> DispatchedEvents => _events;

    public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
    {
        _events.AddRange(events);
        return Task.CompletedTask;
    }
}
