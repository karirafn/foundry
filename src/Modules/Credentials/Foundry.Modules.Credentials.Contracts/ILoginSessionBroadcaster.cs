namespace Foundry.Modules.Credentials.Contracts;

/// <summary>
/// Broadcasts OAuth login session phase transitions to connected dashboard clients.
/// The Credentials module depends on this abstraction; the SignalR implementation lives in WebApi.
/// </summary>
public interface ILoginSessionBroadcaster
{
    Task BroadcastAsync(LoginSessionUpdate update, CancellationToken cancellationToken);
}
