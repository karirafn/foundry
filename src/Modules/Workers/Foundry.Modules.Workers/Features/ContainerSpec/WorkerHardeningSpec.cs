namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>
/// Single source of the Linux hardening every worker container runs under (ADR 0088). The rootless
/// Docker-in-Docker profile (<see cref="RootlessDindRunSpec"/>) relaxes parts of it and documents each relaxation;
/// the Publisher (ADR 0100) runs under it with no capability added back.
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
    /// The only memory-backed writable path: the managed settings are a few kilobytes. <c>/etc/claude-code</c> is
    /// created root-owned, so only the root phase can write the managed settings into it.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> TmpfsMounts = new Dictionary<string, string>
    {
        [ManagedSettingsDirectory] = "rw,nosuid,nodev,noexec,mode=0755",
    };

    /// <summary>
    /// The temp directories, which agents fill with worktrees, tool installs and caches. On tmpfs those pages would
    /// count against the container's memory limit, so a full temp directory meant an OOM kill or ENOSPC (#821).
    /// Anonymous volumes inherit the image's content, ownership and mode on first mount (so <c>/tmp</c> and
    /// <c>/var/tmp</c> stay sticky <c>1777</c> and allow exec for the Publisher's <c>GIT_ASKPASS</c> helper) and are
    /// removed with the container.
    /// </summary>
    public static readonly IReadOnlyList<string> TempScratchVolumePaths = ["/tmp", "/var/tmp"];

    /// <summary>
    /// Disk-backed writable paths for the clone, the worker's home (package caches, build output, tool config) and
    /// the temp directories (<see cref="TempScratchVolumePaths"/>).
    /// </summary>
    public static readonly IReadOnlyList<string> ScratchVolumePaths = ["/workspace", "/home/node", .. TempScratchVolumePaths];

    /// <summary>Where the Publisher keeps its scratch repository, on an anonymous volume removed with it.</summary>
    public const string PublisherWorkPath = "/work";

    /// <summary>
    /// The Publisher runs as an unprivileged user from the start, so it needs no capability back; it writes only its
    /// scratch repository, the temp directories and the Hand-off volume.
    /// </summary>
    public static readonly IReadOnlyList<string> PublisherScratchVolumePaths = [PublisherWorkPath, .. TempScratchVolumePaths];

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

    public static WorkerContainerSpec ApplyToPublisher(WorkerContainerSpec spec) =>
        spec with
        {
            DroppedCapabilities = DroppedCapabilities,
            Capabilities = [],
            SecurityOptions = SecurityOptions,
            ReadOnlyRootFilesystem = ReadOnlyRootFilesystem,
            TmpfsMounts = new Dictionary<string, string>(),
            ScratchVolumePaths = PublisherScratchVolumePaths,
        };
}
