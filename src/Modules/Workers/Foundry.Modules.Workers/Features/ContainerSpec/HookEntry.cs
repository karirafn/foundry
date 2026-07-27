namespace Foundry.Modules.Workers.Features.ContainerSpec;

internal sealed class HookEntry
{
    public string Type { get; set; } = "command";

    public string Command { get; set; } = string.Empty;
}
