using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Features.ImageBuild;

internal interface IWorkerImageRebuildQueue
{
    /// <summary>
    /// Raised before the enqueue when an immediate rebuild is requested, allowing the service to
    /// cancel any pending delayed retry and reset its attempt counter.
    /// </summary>
    event Action? ImmediateRebuildRequested;

    /// <summary>
    /// Raises <see cref="ImmediateRebuildRequested"/> and then enqueues a <see cref="ImageRebuildOrigin.Requested"/>
    /// rebuild, superseding any pending delayed retry.
    /// </summary>
    void RequestImmediateRebuild();

    /// <summary>
    /// Enqueues a rebuild. Returns false when a rebuild is already pending (bounded capacity 1): the request merges
    /// into it, and the merged rebuild is <see cref="ImageRebuildOrigin.Requested"/> when either request was.
    /// </summary>
    bool TryEnqueue(ImageRebuildOrigin origin);

    /// <summary>
    /// Reads pending rebuilds, each with the origin it ran for. Completes when the channel is closed.
    /// </summary>
    IAsyncEnumerable<ImageRebuildOrigin> ReadAllAsync(CancellationToken cancellationToken);
}
