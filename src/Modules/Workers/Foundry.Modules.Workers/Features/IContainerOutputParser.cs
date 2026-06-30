using Foundry.Modules.Workers.Domain;

namespace Foundry.Modules.Workers.Features;

internal interface IContainerOutputParser
{
    ContainerOutputParseResult Parse(string? log, int defaultCooldownMinutes);

    RunResultSummary? ParseRunResultSummary(string? log);
}
