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
        await ImageBuildBroadcastDelivery.DeliverAsync(settings, integrationEventProcessor, cancellationToken);
    }
}
