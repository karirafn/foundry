using Foundry.Modules.Workers.Contracts;

using Microsoft.AspNetCore.SignalR;

namespace Foundry.WebApi.Hubs;

internal sealed class SignalRAgentActivityBroadcaster(IHubContext<WorkerHub> hubContext)
    : IAgentActivityBroadcaster
{
    public Task BroadcastActivityAsync(AgentActivity activity, CancellationToken cancellationToken)
        => hubContext.Clients.All.SendAsync(WorkerHub.AgentActivityMethod, activity, cancellationToken);

    public Task BroadcastEndedAsync(AgentActivityEnded ended, CancellationToken cancellationToken)
        => hubContext.Clients.All.SendAsync(WorkerHub.AgentActivityEndedMethod, ended, cancellationToken);
}
