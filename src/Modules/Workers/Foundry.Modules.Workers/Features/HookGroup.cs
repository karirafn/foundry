namespace Foundry.Modules.Workers.Features;

internal sealed class HookGroup
{
    public string? Matcher { get; set; }

    public List<HookEntry> Hooks { get; set; } = [];
}
