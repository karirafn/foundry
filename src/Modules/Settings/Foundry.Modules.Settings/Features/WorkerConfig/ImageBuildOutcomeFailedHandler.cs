using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features.WorkerConfig;

internal sealed class ImageBuildOutcomeFailedHandler(
    DbContext dbContext,
    IIntegrationEventProcessor integrationEventProcessor)
    : IIntegrationEventHandler<ImageBuildOutcomeFailed>
{
    public async Task HandleAsync(ImageBuildOutcomeFailed @event, CancellationToken cancellationToken)
    {
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            return;
        }

        settings.FailImageBuild(@event.ErrorTail, @event.NextRetryAt, @event.Attempt);
        await dbContext.SaveChangesAsync(cancellationToken);
        await DeliverBroadcastEventsAsync(settings, cancellationToken);
    }

    /// <summary>
    /// Drains the aggregate's integration events (ephemeral broadcast events raised by the
    /// domain transition method) and delivers them directly via
    /// <see cref="IIntegrationEventProcessor"/> — bypassing the outbox — because these events
    /// have only transient SignalR broadcast consumers, no durable DB side-effects.
    /// A fresh <see cref="Guid"/> is used as the event-id because inbox dedup is a no-op for
    /// one-shot direct delivery.
    /// </summary>
    private async Task DeliverBroadcastEventsAsync(
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        foreach (IIntegrationEvent broadcastEvent in settings.IntegrationEvents)
        {
            await integrationEventProcessor.ProcessAsync(
                Guid.NewGuid(),
                broadcastEvent,
                cancellationToken);
        }

        settings.ClearIntegrationEvents();
    }
}
