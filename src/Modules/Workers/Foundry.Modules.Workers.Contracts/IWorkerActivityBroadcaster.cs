namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Broadcasts worker activity notifications to connected dashboard clients.
/// </summary>
public interface IWorkerActivityBroadcaster
{
    Task BroadcastActivityAsync(WorkerActivity activity, CancellationToken cancellationToken);
}
