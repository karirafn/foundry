using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Outcome;

internal interface IContainerOutputParser
{
    ContainerOutputParseResult Parse(string? log);

    RunResultSummary? ParseRunResultSummary(string? log);

    /// <summary>
    /// Returns the <c>structured_output</c> object of the log's last result event: the role's verdict
    /// (ADR 0094), unvalidated. <see langword="null"/> when there is no result event or it carries no object.
    /// </summary>
    JsonObject? ParseStructuredOutput(string? log);
}
