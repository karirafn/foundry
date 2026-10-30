using Foundry.Modules.Credentials.Features.Usage;

using Microsoft.AspNetCore.SignalR;

namespace Foundry.WebApi.Hubs;

internal sealed class SignalRUsageSnapshotBroadcaster(IHubContext<SystemNotificationHub> hubContext)
    : IUsageSnapshotBroadcaster
{
    private const string UsageSnapshotUpdatedMethod = "UsageSnapshotUpdated";

    public Task SendSnapshotAsync(UsageSnapshot? snapshot, CancellationToken cancellationToken)
        => hubContext.Clients.All.SendAsync(
            UsageSnapshotUpdatedMethod,
            snapshot?.ToResponse(),
            cancellationToken);
}
