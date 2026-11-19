using Foundry.Modules.Monitoring.Contracts.Queries;

using Microsoft.Extensions.Options;

namespace Foundry.Modules.Monitoring.Features.PackageSourceProxy;

internal sealed class PackageSourceProxyAddress(IOptions<PackageSourceProxyOptions> options) : IPackageSourceProxyAddress
{
    public Uri WorkerBaseUrl => options.Value.WorkerBaseUrl;
}
