using Foundry.Modules.Settings.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.ImageBuild;

internal sealed class ImageBuildFailedBroadcastHandler(
    ISystemNotificationBroadcaster broadcaster) : IIntegrationEventHandler<ImageBuildFailed>
{
    public Task HandleAsync(ImageBuildFailed @event, CancellationToken cancellationToken)
    {
        return broadcaster.SendAsync(
            new SystemNotification(
                WorkerImageRebuildService.ImageBuildCategory,
                IsActive: true,
                Message: string.Empty),
            cancellationToken);
    }
}
