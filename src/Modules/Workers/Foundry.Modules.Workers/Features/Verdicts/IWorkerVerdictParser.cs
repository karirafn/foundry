using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Verdicts;

/// <summary>
/// Parses one role's <c>structured_output</c> into its typed verdict (ADR 0094). Each role with a
/// <c>workers/roles/&lt;role&gt;/verdict.schema.json</c> has one parser, changed together with the schema (ADR 0095).
/// </summary>
internal interface IWorkerVerdictParser
{
    WorkerRole Role { get; }

    /// <summary>
    /// Maps the role's structured output to the verdict Foundry records:
    /// <see cref="RunVerdictStatus.Invalid"/> when it fails the typed parse.
    /// </summary>
    RunVerdict Parse(JsonObject structuredOutput);
}
