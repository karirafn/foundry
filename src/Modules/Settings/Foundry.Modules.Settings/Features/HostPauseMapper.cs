using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.ValueObjects;

namespace Foundry.Modules.Settings.Features;

internal static class HostPauseMapper
{
    internal static PausedHost ToPausedHost(HostPause pause) =>
        new(pause.Host, pause.PausedAt, pause.ProbeAttempts, pause.NextProbeAt);
}
