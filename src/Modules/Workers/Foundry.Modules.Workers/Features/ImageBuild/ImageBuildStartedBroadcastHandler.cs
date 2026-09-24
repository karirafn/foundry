using Foundry.Modules.Settings.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.ImageBuild;

[IntegrationEventHandlerIdentity("Workers.ImageBuildStartedBroadcast")]
internal sealed class ImageBuildStartedBroadcastHandler(
    ISystemNotificationBroadcaster broadcaster) : IIntegrationEventHandler<ImageBuildStarted>
{
    public Task HandleAsync(ImageBuildStarted @event, CancellationToken cancellationToken)
    {
        return broadcaster.SendAsync(
            new SystemNotification(
                WorkerImageRebuildService.ImageBuildCategory,
                IsActive: true,
                Message: string.Empty),
            cancellationToken);
    }
}
