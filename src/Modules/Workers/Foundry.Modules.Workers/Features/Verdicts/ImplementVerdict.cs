using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Verdicts;

/// <summary>What an implement run reports: it finished, or it needs answers from a person first.</summary>
internal enum ImplementOutcome
{
    Done,
    NeedsInput,
}

/// <summary>
/// The implement role's typed verdict, mirroring <c>workers/roles/implement/verdict.schema.json</c>:
/// <c>{"outcome": "done" | "needs_input", "questions": [...]}</c>, with <c>questions</c> required for
/// <c>needs_input</c>.
/// </summary>
internal sealed record ImplementVerdict(ImplementOutcome Outcome, IReadOnlyList<RunVerdictQuestion> Questions)
{
    internal const string OutcomeProperty = "outcome";
    internal const string QuestionsProperty = "questions";
    internal const string DoneToken = "done";
    internal const string NeedsInputToken = "needs_input";

    /// <summary>Returns the verdict, or <see langword="null"/> when <paramref name="output"/> breaks the schema.</summary>
    public static ImplementVerdict? TryParse(JsonObject output)
    {
        ArgumentNullException.ThrowIfNull(output);

        foreach (KeyValuePair<string, JsonNode?> property in output)
        {
            if (property.Key is not (OutcomeProperty or QuestionsProperty))
            {
                return null;
            }
        }

        ImplementOutcome? outcome = VerdictQuestionsReader.ReadString(output[OutcomeProperty]) switch
        {
            DoneToken => ImplementOutcome.Done,
            NeedsInputToken => ImplementOutcome.NeedsInput,
            _ => null,
        };

        if (outcome is null)
        {
            return null;
        }

        IReadOnlyList<RunVerdictQuestion> questions = [];

        if (output.ContainsKey(QuestionsProperty))
        {
            if (VerdictQuestionsReader.TryRead(output[QuestionsProperty]) is not { } read)
            {
                return null;
            }

            questions = read;
        }

        if (outcome == ImplementOutcome.NeedsInput && questions.Count == 0)
        {
            return null;
        }

        return new ImplementVerdict(outcome.Value, questions);
    }
}

internal sealed class ImplementVerdictParser : IWorkerVerdictParser
{
    public WorkerRole Role => WorkerRole.Implement;

    public RunVerdict Parse(JsonObject structuredOutput) =>
        ImplementVerdict.TryParse(structuredOutput) switch
        {
            null => RunVerdict.Invalid(),
            { Outcome: ImplementOutcome.NeedsInput } verdict => RunVerdict.NeedsInput(verdict.Questions),
            _ => RunVerdict.Done(),
        };
}
