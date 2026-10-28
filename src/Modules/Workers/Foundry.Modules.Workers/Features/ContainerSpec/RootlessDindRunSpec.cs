using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>
/// Single source of the ADR 0079 relaxed allowance set for rootless Docker-in-Docker in worker containers.
/// Every consumer reads the allowance set from here, including the system-path unmask flag.
/// </summary>
internal static class RootlessDindRunSpec
{
    public static readonly IReadOnlyList<string> Capabilities = ["SYS_ADMIN"];

    public static readonly IReadOnlyList<string> SecurityOptions =
    [
        "seccomp=unconfined",
        "apparmor=unconfined",
    ];

    public static readonly IReadOnlyList<string> Devices = ["/dev/net/tun"];

    // The docker CLI's "--security-opt systempaths=unconfined" is a client-side alias that the Engine API rejects.
    // The orchestrator expresses it as empty MaskedPaths/ReadonlyPaths, which lets the nested rootless dockerd
    // write /proc/sys (net.ipv4.ip_forward).
    public static bool UnmaskSystemPaths => true;

    public static string DataRootContainerPath => WorkerVolumeNames.DataRootContainerPath;
}
