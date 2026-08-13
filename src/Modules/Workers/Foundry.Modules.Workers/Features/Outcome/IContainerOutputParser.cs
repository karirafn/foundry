using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Outcome;

internal interface IContainerOutputParser
{
    ContainerOutputParseResult Parse(string? log);

    RunResultSummary? ParseRunResultSummary(string? log);
}
