using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Foundry.Modules.Workers.Features.ImageBuild;

internal sealed class WorkerImageRebuildQueue : IWorkerImageRebuildQueue
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(capacity: 1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

    public event Action? ImmediateRebuildRequested;

    public void RequestImmediateRebuild()
    {
        // Raise the event before enqueuing so subscribers can cancel pending delayed retries
        // before the new signal enters the channel.
        // The event is raised outside any lock — see csharp rules on events-inside-locks.
        ImmediateRebuildRequested?.Invoke();
        TryEnqueue();
    }

    public bool TryEnqueue()
    {
        // DropWrite silently drops the item when full but still returns true from TryWrite.
        // Check count first so callers know whether the signal was enqueued or dropped.
        if (_channel.Reader.Count >= 1)
        {
            return false;
        }

        return _channel.Writer.TryWrite(true);
    }

    public async IAsyncEnumerable<bool> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (bool signal in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return signal;
        }
    }
}
