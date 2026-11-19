using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.ValueObjects;

namespace Foundry.Modules.Settings.Features;

internal static class PackageProxyPauseMapper
{
    internal static PausedPackageProxy? ToPausedPackageProxy(PackageProxyPause? pause) =>
        pause is null ? null : new(pause.PausedAt, pause.ProbeAttempts, pause.NextProbeAt);
}
