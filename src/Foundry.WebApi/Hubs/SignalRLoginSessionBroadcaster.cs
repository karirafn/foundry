using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Login;

using Microsoft.AspNetCore.SignalR;

namespace Foundry.WebApi.Hubs;

internal sealed class SignalRLoginSessionBroadcaster(IHubContext<SystemNotificationHub> hubContext)
    : ILoginSessionBroadcaster
{
    private const string LoginSessionUpdateMethod = "LoginSessionUpdated";

    public Task BroadcastAsync(LoginSessionUpdate update, CancellationToken cancellationToken)
        => hubContext.Clients.All.SendAsync(LoginSessionUpdateMethod, update, cancellationToken);
}
