namespace Foundry.Modules.Workers.Features.ImageBuild;

internal interface IWorkerImageRebuildQueue
{
    /// <summary>
    /// Raised before the enqueue when an immediate rebuild is requested, allowing the service to
    /// cancel any pending delayed retry and reset its attempt counter.
    /// </summary>
    event Action? ImmediateRebuildRequested;

    /// <summary>
    /// Raises <see cref="ImmediateRebuildRequested"/> and then enqueues a rebuild signal,
    /// superseding any pending delayed retry.
    /// </summary>
    void RequestImmediateRebuild();

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
