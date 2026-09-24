using Foundry.Modules.Settings.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.ImageBuild;

[IntegrationEventHandlerIdentity("Workers.ImageBuildCompletedBroadcast")]
internal sealed class ImageBuildCompletedBroadcastHandler(
    ISystemNotificationBroadcaster broadcaster) : IIntegrationEventHandler<ImageBuildCompleted>
{
    public Task HandleAsync(ImageBuildCompleted @event, CancellationToken cancellationToken)
    {
        return broadcaster.SendAsync(
            new SystemNotification(
                WorkerImageRebuildService.ImageBuildCategory,
                IsActive: false,
                Message: string.Empty),
            cancellationToken);
    }
}
