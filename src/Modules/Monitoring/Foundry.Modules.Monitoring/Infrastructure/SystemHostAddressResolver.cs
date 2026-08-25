using System.Net;
using System.Net.Sockets;

namespace Foundry.Modules.Monitoring.Infrastructure;

internal sealed class SystemHostAddressResolver : IHostAddressResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            return addresses;
        }
        catch (SocketException)
        {
            // An unresolvable host returns no addresses. The allowlist and HTTPS layers still
            // bound the host name — it will simply fail later at the HTTP call, not here.
            return [];
        }
    }
}
