using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.HostPauses;

/// <summary>
/// Tells the dashboard a host pause was added, advanced or cleared, so it reloads its settings. A pure SignalR
/// notification with no durable consumer, so it is sent directly once the change is saved (never through the outbox).
/// Deliberately not <c>DispatchResumed</c>: that event re-queues usage-limited failures, which a host clearing must not.
/// </summary>
internal static class HostPauseBroadcast
{
    private const string DispatchCategory = "dispatch";

    public static async Task SendAsync(
        ISystemNotificationBroadcaster broadcaster,
        bool hostPaused,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await broadcaster.SendAsync(
                new SystemNotification(DispatchCategory, IsActive: hostPaused, Message: string.Empty),
                cancellationToken);
        }
#pragma warning disable CA1031 // A failed SignalR notification must not undo a saved pause change; the dashboard catches up on its next settings read.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Could not broadcast the host-pause change to the dashboard.");
        }
    }
}
