using Foundry.Modules.Credentials.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Credentials.Features.Broadcasts;

internal sealed class CreditsRestoredBroadcastHandler(
    ISystemNotificationBroadcaster broadcaster) : IIntegrationEventHandler<CreditsRestored>
{
    public Task HandleAsync(CreditsRestored @event, CancellationToken cancellationToken)
    {
        // IsActive:false signals the credit block has cleared — clients re-fetch /api/credentials.
        return broadcaster.SendAsync(
            new SystemNotification(NotificationCategories.Credits, IsActive: false, Message: string.Empty),
            cancellationToken);
    }
}
