using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features;

internal sealed class DispatchPausedBroadcastHandler(
    ISystemNotificationBroadcaster broadcaster) : IIntegrationEventHandler<DispatchPaused>
{
    private const string DispatchCategory = "dispatch";

    public Task HandleAsync(DispatchPaused @event, CancellationToken cancellationToken)
    {
        // ResetsAt is intentionally omitted: /api/settings is the single source of truth; clients re-fetch after this notification.
        return broadcaster.SendAsync(
            new SystemNotification(DispatchCategory, IsActive: true, Message: string.Empty),
            cancellationToken);
    }
}
