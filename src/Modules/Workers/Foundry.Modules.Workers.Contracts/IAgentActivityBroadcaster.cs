namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Broadcasts Agent Activity projection changes to connected dashboard clients.
/// </summary>
public interface IAgentActivityBroadcaster
{
    Task BroadcastActivityAsync(AgentActivity activity, CancellationToken cancellationToken);

    Task BroadcastEndedAsync(AgentActivityEnded ended, CancellationToken cancellationToken);
}
