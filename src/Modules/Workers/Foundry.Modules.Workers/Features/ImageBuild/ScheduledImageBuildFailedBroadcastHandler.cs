using Foundry.Modules.Settings.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.ImageBuild;

/// <summary>
/// A failed scheduled rebuild leaves dispatch running (ADR 0127), so the image-build notification goes inactive; the
/// notification still makes the dashboard reload the settings that carry the failure.
/// </summary>
[IntegrationEventHandlerIdentity("Workers.ScheduledImageBuildFailedBroadcast")]
internal sealed class ScheduledImageBuildFailedBroadcastHandler(
    ISystemNotificationBroadcaster broadcaster) : IIntegrationEventHandler<ScheduledImageBuildFailed>
{
    public Task HandleAsync(ScheduledImageBuildFailed @event, CancellationToken cancellationToken)
    {
        return broadcaster.SendAsync(
            new SystemNotification(
                WorkerImageRebuildService.ImageBuildCategory,
                IsActive: false,
                Message: string.Empty),
            cancellationToken);
    }
}
