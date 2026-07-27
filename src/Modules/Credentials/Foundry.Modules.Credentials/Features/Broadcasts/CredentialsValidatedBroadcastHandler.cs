using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Contracts.Events;
using Foundry.Shared;

namespace Foundry.Modules.Credentials.Features.Broadcasts;

internal sealed class CredentialsValidatedBroadcastHandler(
    ISystemNotificationBroadcaster broadcaster) : IIntegrationEventHandler<CredentialsValidated>
{
    private const string DispatchCategory = "dispatch";

    public Task HandleAsync(CredentialsValidated @event, CancellationToken cancellationToken)
    {
        // IsActive:false signals the dispatch has resumed — clients re-fetch /api/credentials.
        return broadcaster.SendAsync(
            new SystemNotification(DispatchCategory, IsActive: false, Message: string.Empty),
            cancellationToken);
    }
}
