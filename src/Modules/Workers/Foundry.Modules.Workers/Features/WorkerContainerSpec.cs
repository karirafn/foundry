namespace Foundry.Modules.Workers.Features;

internal sealed record WorkerContainerSpec(
    string Image,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    IReadOnlyList<BindMount> BindMounts,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<string> Command)
{
    public IReadOnlyList<string> SecurityOptions { get; init; } = [];

    public IReadOnlyList<string> Devices { get; init; } = [];

    public IReadOnlyList<VolumeMount> VolumeMounts { get; init; } = [];
}
