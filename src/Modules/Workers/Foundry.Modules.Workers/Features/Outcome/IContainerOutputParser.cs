using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Outcome;

internal interface IContainerOutputParser
{
    ContainerOutputParseResult Parse(string? log, int defaultCooldownMinutes);

    RunResultSummary? ParseRunResultSummary(string? log);
}
