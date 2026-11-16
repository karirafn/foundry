using System.Threading.Channels;

using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// The Package Sources whose credential changed and that must be probed again at once. A credential save or removal
/// adds the source; <see cref="PackageSourceProbeService"/> probes it outside the request.
/// </summary>
internal sealed class PackageSourceProbeRequests
{
    private readonly Channel<PackageSourceId> _channel = Channel.CreateUnbounded<PackageSourceId>(
        new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<PackageSourceId> Reader => _channel.Reader;

    public void Request(PackageSourceId id)
    {
        // An unbounded channel that is never completed always accepts a write.
        _channel.Writer.TryWrite(id);
    }
}
