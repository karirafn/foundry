namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Pushes the current usage snapshot (or its absence) to connected clients via the system hub.
/// <para>
/// Implementations are provided by the WebApi host (SignalR); the Credentials module depends only
/// on this abstraction. The host has <c>InternalsVisibleTo</c> access to the Credentials module,
/// so it can see <see cref="UsageSnapshot"/> and map it to the wire DTO before broadcasting.
/// </para>
/// </summary>
internal interface IUsageSnapshotBroadcaster
{
    /// <summary>
    /// Sends the snapshot to all connected clients. Pass <see langword="null"/> to signal
    /// "no data" (cleared or not yet polled).
    /// </summary>
    Task SendSnapshotAsync(UsageSnapshot? snapshot, CancellationToken cancellationToken);
}
