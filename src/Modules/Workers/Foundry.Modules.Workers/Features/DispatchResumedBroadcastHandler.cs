using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features;

internal sealed class DispatchResumedBroadcastHandler(
    ISystemNotificationBroadcaster broadcaster) : IIntegrationEventHandler<DispatchResumed>
{
    private const string DispatchCategory = "dispatch";

    public Task HandleAsync(DispatchResumed @event, CancellationToken cancellationToken)
    {
        return broadcaster.SendAsync(
            new SystemNotification(DispatchCategory, IsActive: false, Message: string.Empty),
            cancellationToken);
    }
}
