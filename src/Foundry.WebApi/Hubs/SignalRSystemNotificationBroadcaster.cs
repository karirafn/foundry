using Foundry.Shared;

using Microsoft.AspNetCore.SignalR;

namespace Foundry.WebApi.Hubs;

internal sealed class SignalRSystemNotificationBroadcaster(IHubContext<SystemNotificationHub> hubContext)
    : ISystemNotificationBroadcaster
{
    private const string SystemNotificationReceivedMethod = "SystemNotificationReceived";

    public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
        => hubContext.Clients.All.SendAsync(SystemNotificationReceivedMethod, notification, cancellationToken);
}
