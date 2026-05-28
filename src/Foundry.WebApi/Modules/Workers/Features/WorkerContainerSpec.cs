namespace Foundry.WebApi.Modules.Workers.Features;

public sealed record WorkerContainerSpec(
    string Image,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    IReadOnlyList<BindMount> BindMounts,
    IReadOnlyDictionary<string, string> Labels);
