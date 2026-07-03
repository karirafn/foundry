using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features;

internal sealed class DispatchPausedForAuthInvalidBroadcastHandler(
    ISystemNotificationBroadcaster broadcaster) : IIntegrationEventHandler<DispatchPausedForAuthInvalid>
{
    private const string DispatchCategory = "dispatch";

    public Task HandleAsync(DispatchPausedForAuthInvalid @event, CancellationToken cancellationToken)
    {
        // ResetsAt is not applicable here; /api/settings is the single source of truth — clients re-fetch after this notification.
        return broadcaster.SendAsync(
            new SystemNotification(DispatchCategory, IsActive: true, Message: string.Empty),
            cancellationToken);
    }
}
