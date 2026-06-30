using Foundry.Modules.Workers.Contracts;

using Microsoft.AspNetCore.SignalR;

namespace Foundry.WebApi.Hubs;

internal sealed class SignalRWorkerActivityBroadcaster(IHubContext<WorkerHub> hubContext)
    : IWorkerActivityBroadcaster
{
    private const string WorkerActivityMethod = "WorkerActivity";

    public Task BroadcastActivityAsync(WorkerActivity activity, CancellationToken cancellationToken)
        => hubContext.Clients.All.SendAsync(WorkerActivityMethod, activity, cancellationToken);
}
