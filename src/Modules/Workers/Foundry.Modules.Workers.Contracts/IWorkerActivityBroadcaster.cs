namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Broadcasts worker activity notifications to connected dashboard clients.
/// Step 8 will formalize the method signature and payload.
/// </summary>
public interface IWorkerActivityBroadcaster
{
    Task BroadcastActivityAsync(WorkerActivity activity, CancellationToken cancellationToken);
}
