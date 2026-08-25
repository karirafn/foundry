using System.Net;

namespace Foundry.Modules.Monitoring.Infrastructure;

internal interface IHostAddressResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}
