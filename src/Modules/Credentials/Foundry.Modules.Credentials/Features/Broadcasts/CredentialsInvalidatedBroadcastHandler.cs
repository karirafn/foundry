using Foundry.Modules.Credentials.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Credentials.Features.Broadcasts;

internal sealed class CredentialsInvalidatedBroadcastHandler(
    ISystemNotificationBroadcaster broadcaster) : IIntegrationEventHandler<CredentialsInvalidated>
{
    private const string DispatchCategory = "dispatch";

    public Task HandleAsync(CredentialsInvalidated @event, CancellationToken cancellationToken)
    {
        // IsActive:true signals the dispatch is paused — clients re-fetch /api/credentials.
        return broadcaster.SendAsync(
            new SystemNotification(DispatchCategory, IsActive: true, Message: string.Empty),
            cancellationToken);
    }
}
