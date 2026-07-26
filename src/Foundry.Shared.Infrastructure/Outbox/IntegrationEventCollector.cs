using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Shared.Infrastructure.Outbox;

public sealed class IntegrationEventCollector
{
    private readonly List<OutboxMessage> _pending = [];

    public bool HasPending => _pending.Count > 0;

    public int PendingCount => _pending.Count;

    public void Enqueue(IIntegrationEvent @event)
    {
        OutboxMessage message = OutboxMessage.Create(@event, DateTimeOffset.UtcNow);
        _pending.Add(message);
    }

    public void DrainInto(DbContext context)
    {
        foreach (OutboxMessage message in _pending)
        {
            context.Set<OutboxMessage>().Add(message);
        }

        _pending.Clear();
    }
}
