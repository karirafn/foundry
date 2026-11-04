namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>
/// Single source of the Linux hardening every worker container runs under (ADR 0088). The rootless
/// Docker-in-Docker profile (<see cref="RootlessDindRunSpec"/>) relaxes parts of it and documents each relaxation.
/// </summary>
internal static class WorkerHardeningSpec
{
    /// <summary>Docker's default capability set is dropped wholesale; only the root phase's needs are added back.</summary>
    public static readonly IReadOnlyList<string> DroppedCapabilities = ["ALL"];

    /// <summary>
    /// The entrypoint's root phase renders the root-owned managed settings, then drops to <c>node</c> through
    /// <c>setpriv --reuid --regid --init-groups</c>, which needs <c>SETUID</c> and <c>SETGID</c>. Nothing after the
    /// drop holds any capability: <c>setpriv</c> clears the inheritable set and the non-root uid keeps none.
    /// </summary>
    public static readonly IReadOnlyList<string> Capabilities = ["SETUID", "SETGID"];

    /// <summary>
    /// <c>no-new-privileges</c> stops a setuid or file-capability binary from regaining what was dropped. Seccomp is
    /// left to the daemon's default profile, which applies whenever no <c>seccomp=</c> option is given.
    /// </summary>
    public static readonly IReadOnlyList<string> SecurityOptions = ["no-new-privileges:true"];

    public static bool ReadOnlyRootFilesystem => true;

    public const string ManagedSettingsDirectory = "/etc/claude-code";

    /// <summary>
    /// Small, memory-backed writable paths. <c>/tmp</c> keeps exec because the entrypoint runs its clone
    /// <c>GIT_ASKPASS</c> helper from there. <c>/etc/claude-code</c> is created root-owned, so only the root phase
    /// can write the managed settings into it.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> TmpfsMounts = new Dictionary<string, string>
    {
        ["/tmp"] = "rw,nosuid,nodev,exec,mode=1777",
        ["/var/tmp"] = "rw,nosuid,nodev,exec,mode=1777",
        [ManagedSettingsDirectory] = "rw,nosuid,nodev,noexec,mode=0755",
    };

    /// <summary>
    /// Disk-backed writable paths for the clone and the worker's home (package caches, build output, tool config).
    /// Anonymous volumes inherit the image's content and ownership on first mount and are removed with the container.
    /// </summary>
    public static readonly IReadOnlyList<string> ScratchVolumePaths = ["/workspace", "/home/node"];

    public static WorkerContainerSpec Apply(WorkerContainerSpec spec) =>
        spec with
        {
            DroppedCapabilities = DroppedCapabilities,
            Capabilities = Capabilities,
            SecurityOptions = SecurityOptions,
            ReadOnlyRootFilesystem = ReadOnlyRootFilesystem,
            TmpfsMounts = TmpfsMounts,
            ScratchVolumePaths = ScratchVolumePaths,
        };
}
