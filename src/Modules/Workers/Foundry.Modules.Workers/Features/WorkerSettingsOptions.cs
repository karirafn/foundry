namespace Foundry.Modules.Workers.Features;

internal sealed class WorkerSettingsOptions
{
    public string? Model { get; set; }

    public IReadOnlyList<string> AdditionalDenyRules { get; set; } = [];

    public Dictionary<string, List<HookGroup>> Hooks { get; set; } = [];
}
