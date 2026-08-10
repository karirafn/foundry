using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features.WorkerConfig;

internal sealed class ImageBuildRequestedHandler(
    DbContext dbContext,
    IIntegrationEventProcessor integrationEventProcessor)
    : IIntegrationEventHandler<ImageBuildRequested>
{
    public async Task HandleAsync(ImageBuildRequested @event, CancellationToken cancellationToken)
    {
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            return;
        }

        settings.BeginImageBuild();
        await dbContext.SaveChangesAsync(cancellationToken);
        await ImageBuildBroadcastDelivery.DeliverAsync(settings, integrationEventProcessor, cancellationToken);
    }
}
