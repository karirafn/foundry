namespace Foundry.Modules.Workers.Features.ContainerSpec;

internal sealed record WorkerContainerSpec(
    string Image,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<string> Command)
{
    /// <summary>Replaces the image's entrypoint when set; <see langword="null"/> keeps it.</summary>
    public IReadOnlyList<string>? Entrypoint { get; init; }

    /// <summary>The user the container starts as when set; <see langword="null"/> keeps the image's.</summary>
    public string? User { get; init; }

    public required int MemoryLimitMb { get; init; }

    public required double CpuLimit { get; init; }

    public required int PidsLimit { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public IReadOnlyList<string> DroppedCapabilities { get; init; } = [];

    public IReadOnlyList<string> SecurityOptions { get; init; } = [];

    public IReadOnlyList<string> Devices { get; init; } = [];

    public bool UnmaskSystemPaths { get; init; }

    public bool ReadOnlyRootFilesystem { get; init; }

    /// <summary>Memory-backed mounts, keyed by container path, valued by their mount options.</summary>
    public IReadOnlyDictionary<string, string> TmpfsMounts { get; init; } = new Dictionary<string, string>();

    /// <summary>Container paths backed by anonymous volumes, which are removed together with the container.</summary>
    public IReadOnlyList<string> ScratchVolumePaths { get; init; } = [];

    public IReadOnlyList<VolumeMount> VolumeMounts { get; init; } = [];

    /// <summary>Extra <c>/etc/hosts</c> entries, each <c>name:address</c>; Docker resolves <c>host-gateway</c> to the host.</summary>
    public IReadOnlyList<string> ExtraHosts { get; init; } = [];
}
