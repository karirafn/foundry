namespace Foundry.Modules.Workers.Features.ContainerSpec;

internal sealed class HookGroup
{
    public string? Matcher { get; set; }

    public List<HookEntry> Hooks { get; set; } = [];
}
