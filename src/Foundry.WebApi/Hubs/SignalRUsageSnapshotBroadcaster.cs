using Foundry.Modules.Credentials.Features.Usage;

using Microsoft.AspNetCore.SignalR;

namespace Foundry.WebApi.Hubs;

internal sealed class SignalRUsageSnapshotBroadcaster(IHubContext<SystemNotificationHub> hubContext)
    : IUsageSnapshotBroadcaster
{
    // A dedicated hub method rather than a SystemNotification category: the client adds every
    // notification category to its banner list, so usage pushes would show as empty banners.
    private const string UsageSnapshotUpdatedMethod = "UsageSnapshotUpdated";

    public Task SendSnapshotAsync(UsageSnapshot? snapshot, CancellationToken cancellationToken)
        => hubContext.Clients.All.SendAsync(
            UsageSnapshotUpdatedMethod,
            snapshot?.ToResponse(),
            cancellationToken);
}
