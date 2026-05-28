namespace Foundry.WebApi.Modules.Workers.Features;

public sealed record BindMount(string HostPath, string ContainerPath);

public sealed record WorkerContainerSpec(
    string Image,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    IReadOnlyList<BindMount> BindMounts,
    IReadOnlyDictionary<string, string> Labels);
