namespace Foundry.Modules.Workers.Features.ImageBuild;

internal interface IWorkerImageRebuildQueue
{
    /// <summary>
    /// Enqueues a rebuild signal. Returns true if the signal was accepted, false if it was dropped
    /// because a rebuild is already pending (bounded capacity 1).
    /// </summary>
    bool TryEnqueue();

    /// <summary>
    /// Reads rebuild signals as they arrive. Completes when the channel is closed.
    /// </summary>
    IAsyncEnumerable<bool> ReadAllAsync(CancellationToken cancellationToken);
}
