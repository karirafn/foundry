namespace Foundry.Modules.Workers.Features;

internal interface IContainerOutputParser
{
    ContainerOutputParseResult Parse(string? log, int defaultCooldownMinutes);
}
