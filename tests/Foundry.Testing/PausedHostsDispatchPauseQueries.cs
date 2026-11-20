using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;

namespace Foundry.Testing;

/// <summary>
/// <see cref="IDispatchPauseQueries"/> whose dispatch-pause state holds only the given provider-host pauses and, when
/// set, <see cref="PackageProxyPause"/>.
/// </summary>
public sealed class PausedHostsDispatchPauseQueries(params PausedHost[] pausedHosts) : IDispatchPauseQueries
{
    public PausedPackageProxy? PackageProxyPause { get; init; }

    public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
        => Task.FromResult(new DispatchPauseState(null, false, pausedHosts, PackageProxyPause));
}
