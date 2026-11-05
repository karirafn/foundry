using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>
/// Single source of the ADR 0079 relaxed allowance set for rootless Docker-in-Docker in worker containers.
/// Every consumer reads the allowance set from here, including the system-path unmask flag. It is applied on top of
/// <see cref="WorkerHardeningSpec"/> (ADR 0088): all capabilities stay dropped except the ones listed here, and the
/// read-only root filesystem with its writable mounts is kept.
/// </summary>
internal static class RootlessDindRunSpec
{
    /// <summary>
    /// <c>SETUID</c>/<c>SETGID</c> serve both the entrypoint's drop to <c>node</c> and the setuid
    /// <c>newuidmap</c>/<c>newgidmap</c>, which also need <c>DAC_OVERRIDE</c> to open the node-owned
    /// <c>/proc/&lt;pid&gt;/uid_map</c> and <c>SYS_ADMIN</c> to write it. They are the image's only setuid binaries,
    /// so nothing else running as <c>node</c> can raise these from the bounding set.
    /// </summary>
    public static readonly IReadOnlyList<string> Capabilities = ["SETUID", "SETGID", "DAC_OVERRIDE", "SYS_ADMIN"];

    /// <summary>
    /// Replaces <see cref="WorkerHardeningSpec.SecurityOptions"/>: <c>no-new-privileges</c> would stop the setuid
    /// <c>newuidmap</c>/<c>newgidmap</c> from gaining the capabilities above, and the daemon's default seccomp
    /// profile blocks the <c>keyctl</c> session keyring that the nested runc creates for every container.
    /// </summary>
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
