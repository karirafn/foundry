using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Features.WorkerConfig;

/// <summary>
/// Drains the aggregate's integration events (ephemeral broadcast events raised by the
/// domain transition method) and delivers them directly via
/// <see cref="IIntegrationEventProcessor"/> — bypassing the outbox — because these events
/// have only transient SignalR broadcast consumers, no durable DB side-effects.
/// A fresh <see cref="Guid"/> is used as the event-id because inbox dedup is a no-op for
/// one-shot direct delivery.
/// </summary>
internal static class ImageBuildBroadcastDelivery
{
    internal static async Task DeliverAsync(
        GlobalSettings settings,
        IIntegrationEventProcessor processor,
        CancellationToken cancellationToken)
    {
        foreach (IIntegrationEvent broadcastEvent in settings.IntegrationEvents)
        {
            await processor.ProcessAsync(
                Guid.NewGuid(),
                broadcastEvent,
                cancellationToken);
        }

        settings.ClearIntegrationEvents();
    }
}
