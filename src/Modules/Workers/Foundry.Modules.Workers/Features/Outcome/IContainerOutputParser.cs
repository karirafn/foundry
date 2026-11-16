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

    /// <summary>
    /// Returns the toolchain the container reported on its <c>FOUNDRY_TOOLCHAIN</c> line (ADR 0109), or
    /// <see langword="null"/> unless the log holds exactly one valid line. Reads the whole log, not its tail: the line is
    /// written at startup, so a long run's tail no longer holds it.
    /// </summary>
    Toolchain? ParseToolchain(string? fullLog);
}
