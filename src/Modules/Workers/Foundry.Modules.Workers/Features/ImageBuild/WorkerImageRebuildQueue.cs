using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Features.ImageBuild;

internal sealed class WorkerImageRebuildQueue : IWorkerImageRebuildQueue
{
    // Capacity 1 with DropWrite collapses rapid settings saves into one rebuild: the pending rebuild
    // reads the latest persisted settings when it starts.
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(capacity: 1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

    // The origin of the pending rebuild lives beside the signal so a later request can upgrade it in place;
    // a channel item cannot be replaced once written.
    private readonly Lock _pendingLock = new();
    private ImageRebuildOrigin? _pendingOrigin;

    public event Action? ImmediateRebuildRequested;

    public void RequestImmediateRebuild()
    {
        // Raise the event before enqueuing so subscribers can cancel pending delayed retries
        // before the new signal enters the channel.
        // The event is raised outside any lock — see csharp rules on events-inside-locks.
        ImmediateRebuildRequested?.Invoke();
        TryEnqueue(ImageRebuildOrigin.Requested);
    }

    public bool TryEnqueue(ImageRebuildOrigin origin)
    {
        lock (_pendingLock)
        {
            if (_pendingOrigin is ImageRebuildOrigin pending)
            {
                _pendingOrigin = Merge(pending, origin);
                return false;
            }

            _pendingOrigin = origin;
        }

        return _channel.Writer.TryWrite(true);
    }

    public async IAsyncEnumerable<ImageRebuildOrigin> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (bool _ in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            ImageRebuildOrigin? origin;
            lock (_pendingLock)
            {
                origin = _pendingOrigin;
                _pendingOrigin = null;
            }

            if (origin is ImageRebuildOrigin taken)
            {
                yield return taken;
            }
        }
    }

    // A requested rebuild's failure must stop dispatch, so it is never downgraded by merging a scheduled one into it.
    private static ImageRebuildOrigin Merge(ImageRebuildOrigin pending, ImageRebuildOrigin incoming) =>
        pending is ImageRebuildOrigin.Requested || incoming is ImageRebuildOrigin.Requested
            ? ImageRebuildOrigin.Requested
            : ImageRebuildOrigin.Scheduled;
}
