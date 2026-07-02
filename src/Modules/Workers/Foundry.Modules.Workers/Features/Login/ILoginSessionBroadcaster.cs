using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Features.Login;

/// <summary>
/// Broadcasts OAuth login session phase transitions to connected dashboard clients.
/// The Workers module depends on this abstraction; the SignalR implementation lives in WebApi.
/// </summary>
public interface ILoginSessionBroadcaster
{
    Task BroadcastAsync(LoginSessionUpdate update, CancellationToken cancellationToken);
}
