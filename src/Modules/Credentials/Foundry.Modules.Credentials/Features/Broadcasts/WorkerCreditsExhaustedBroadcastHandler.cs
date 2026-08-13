using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Credentials.Features.Broadcasts;

internal sealed class WorkerCreditsExhaustedBroadcastHandler(
    ISystemNotificationBroadcaster broadcaster) : IIntegrationEventHandler<WorkerCreditsExhausted>
{
    public Task HandleAsync(WorkerCreditsExhausted @event, CancellationToken cancellationToken)
    {
        // IsActive:true signals the credit block is active — clients re-fetch /api/credentials.
        return broadcaster.SendAsync(
            new SystemNotification(NotificationCategories.Credits, IsActive: true, Message: string.Empty),
            cancellationToken);
    }
}
