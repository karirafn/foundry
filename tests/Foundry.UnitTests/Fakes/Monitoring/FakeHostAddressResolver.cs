using System.Net;

using Foundry.Modules.Monitoring.Infrastructure;

namespace Foundry.UnitTests.Fakes.Monitoring;

/// <summary>
/// In-memory fake of <see cref="IHostAddressResolver"/> with a seeded host→addresses map.
/// </summary>
internal sealed class FakeHostAddressResolver : IHostAddressResolver
{
    private readonly Dictionary<string, IReadOnlyList<IPAddress>> _map = [];

    public FakeHostAddressResolver WithAddresses(string host, params IPAddress[] addresses)
    {
        _map[host] = addresses;
        return this;
    }

    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (_map.TryGetValue(host, out IReadOnlyList<IPAddress>? addresses))
        {
            return Task.FromResult(addresses);
        }

        return Task.FromResult<IReadOnlyList<IPAddress>>([]);
    }
}
