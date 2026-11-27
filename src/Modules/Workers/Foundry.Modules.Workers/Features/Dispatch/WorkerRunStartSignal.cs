using System.Threading.Channels;

namespace Foundry.Modules.Workers.Features.Dispatch;

/// <summary>Wakes <see cref="WorkerRunStarter"/> when a <see cref="Domain.Entities.States.StartingRun"/> is ready to start.</summary>
internal sealed class WorkerRunStartSignal
{
    // Capacity 1 with DropWrite collapses repeated signals into one wake: each wake reads every ready run.
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(capacity: 1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

    public ChannelReader<bool> Reader => _channel.Reader;

    public void Signal() => _channel.Writer.TryWrite(true);
}
