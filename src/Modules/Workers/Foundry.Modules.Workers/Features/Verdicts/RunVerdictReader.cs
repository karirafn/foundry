using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;

namespace Foundry.Modules.Workers.Features.Verdicts;

/// <summary>
/// Reads a finished run's verdict (ADR 0094): extracts the last result event's <c>structured_output</c> generically,
/// then parses it with the run role's typed verdict parser. The run's outcome is decided from facts (merge request,
/// commits, exit code); only a schema-valid <c>needs_input</c> verdict on an exit-0 run changes it (ADR 0096).
/// </summary>
internal sealed class RunVerdictReader(
    IContainerOutputParser containerOutputParser,
    IEnumerable<IWorkerVerdictParser> parsers)
{
    private readonly IReadOnlyList<IWorkerVerdictParser> _parsers = [.. parsers];

    /// <summary>
    /// Returns the verdict to record, or <see langword="null"/> when the role has no verdict parser.
    /// A missing <c>structured_output</c> is <see cref="RunVerdictStatus.Missing"/>.
    /// </summary>
    public RunVerdict? Read(WorkerRole role, string? containerOutput)
    {
        ArgumentNullException.ThrowIfNull(role);

        IWorkerVerdictParser? parser = _parsers.FirstOrDefault(candidate => candidate.Role == role);

        if (parser is null)
        {
            return null;
        }

        JsonObject? structuredOutput = containerOutputParser.ParseStructuredOutput(containerOutput);

        return structuredOutput is null ? RunVerdict.Missing() : parser.Parse(structuredOutput);
    }
}
