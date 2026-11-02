namespace Foundry.Modules.Workers.Features.ContainerSpec;

internal sealed class WorkerSettingsOptions
{
    public Dictionary<string, List<HookGroup>> Hooks { get; set; } = [];
}
