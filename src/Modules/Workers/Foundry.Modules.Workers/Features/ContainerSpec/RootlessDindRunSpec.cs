using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>
/// Single source of the ADR 0079 relaxed allowance set for rootless Docker-in-Docker in worker containers.
/// All values here are authoritative — do not duplicate them elsewhere.
/// </summary>
internal static class RootlessDindRunSpec
{
    public static readonly IReadOnlyList<string> Capabilities = ["SYS_ADMIN"];

    public static readonly IReadOnlyList<string> SecurityOptions =
    [
        "seccomp=unconfined",
        "apparmor=unconfined",
        "systempaths=unconfined",
    ];

    public static readonly IReadOnlyList<string> Devices = ["/dev/net/tun"];

    public static string DataRootContainerPath => WorkerVolumeNames.DataRootContainerPath;
}
