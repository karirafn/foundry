using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.DispatchPauses;

internal sealed class DispatchPauseQueries(DbContext dbContext) : IDispatchPauseQueries
{
    public async Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
    {
        DispatchPause? pause = await dbContext.FindDispatchPauseNoTrackingAsync(cancellationToken);

        return pause is null ? new DispatchPauseState(null, false, []) : ToState(pause);
    }

    internal static DispatchPauseState ToState(DispatchPause pause) =>
        new(
            pause.UsageLimitResetsAt,
            pause.IsDispatchPaused,
            [.. pause.HostPauses.Select(ToPausedHost)],
            ToPausedPackageProxy(pause.PackageProxyPause));

    private static PausedHost ToPausedHost(HostPause pause) =>
        new(pause.Host, pause.PausedAt, pause.ProbeAttempts, pause.NextProbeAt);

    private static PausedPackageProxy? ToPausedPackageProxy(PackageProxyPause? pause) =>
        pause is null ? null : new(pause.PausedAt, pause.ProbeAttempts, pause.NextProbeAt);
}
