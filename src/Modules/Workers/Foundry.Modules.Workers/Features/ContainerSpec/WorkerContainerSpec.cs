namespace Foundry.Modules.Workers.Features.ContainerSpec;

internal sealed record WorkerContainerSpec(
    string Image,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    IReadOnlyList<BindMount> BindMounts,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<string> Command)
{
    public required int MemoryLimitMb { get; init; }

    public required double CpuLimit { get; init; }

    public required int PidsLimit { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public IReadOnlyList<string> SecurityOptions { get; init; } = [];

    public IReadOnlyList<string> Devices { get; init; } = [];

    public bool UnmaskSystemPaths { get; init; }

    public IReadOnlyList<VolumeMount> VolumeMounts { get; init; } = [];
}
