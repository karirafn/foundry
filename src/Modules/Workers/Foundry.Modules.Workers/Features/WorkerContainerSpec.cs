namespace Foundry.Modules.Workers.Features;

internal sealed record WorkerContainerSpec(
    string Image,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    IReadOnlyList<BindMount> BindMounts,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<string> Command);
