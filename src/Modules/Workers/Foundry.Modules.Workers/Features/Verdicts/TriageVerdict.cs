using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Verdicts;

/// <summary>What kind of change a triaged issue asks for.</summary>
internal enum TriageIssueKind
{
    Feature,
    Bug,
    Refactor,
    Documentation,
}

/// <summary>
/// The triage role's typed verdict, mirroring <c>workers/roles/triage/verdict.schema.json</c>:
/// <c>{"outcome": "ready" | "needs_input", "kind": "feature" | "bug" | "refactor" | "documentation", "questions": [...]}</c>,
/// with <c>kind</c> always required and <c>questions</c> required for <c>needs_input</c>.
/// </summary>
internal abstract record TriageVerdict(TriageIssueKind Kind)
{
    internal const string OutcomeProperty = "outcome";
    internal const string KindProperty = "kind";
    internal const string QuestionsProperty = "questions";
    internal const string ReadyToken = "ready";
    internal const string NeedsInputToken = "needs_input";

    /// <summary>The <c>kind</c> tokens of the schema and the kind each stands for.</summary>
    internal static readonly IReadOnlyDictionary<string, TriageIssueKind> KindTokens =
        new Dictionary<string, TriageIssueKind>(StringComparer.Ordinal)
        {
            ["feature"] = TriageIssueKind.Feature,
            ["bug"] = TriageIssueKind.Bug,
            ["refactor"] = TriageIssueKind.Refactor,
            ["documentation"] = TriageIssueKind.Documentation,
        };

    /// <summary>The issue can be implemented as written.</summary>
    internal sealed record Ready(TriageIssueKind Kind) : TriageVerdict(Kind);

    /// <summary>The issue cannot be implemented without answers from a person.</summary>
    internal sealed record NeedsInput(TriageIssueKind Kind, IReadOnlyList<RunVerdictQuestion> Questions) : TriageVerdict(Kind);

    /// <summary>Returns the verdict, or <see langword="null"/> when <paramref name="output"/> breaks the schema.</summary>
    public static TriageVerdict? TryParse(JsonObject output)
    {
        ArgumentNullException.ThrowIfNull(output);

        foreach (KeyValuePair<string, JsonNode?> property in output)
        {
            if (property.Key is not (OutcomeProperty or KindProperty or QuestionsProperty))
            {
                return null;
            }
        }

        if (VerdictQuestionsReader.ReadString(output[KindProperty]) is not { } kindToken
            || !KindTokens.TryGetValue(kindToken, out TriageIssueKind kind))
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

        return VerdictQuestionsReader.ReadString(output[OutcomeProperty]) switch
        {
            ReadyToken => new Ready(kind),
            NeedsInputToken when questions.Count > 0 => new NeedsInput(kind, questions),
            _ => null,
        };
    }
}

/// <summary>
/// Records a triage verdict: <c>ready</c> as <see cref="RunVerdictStatus.Done"/>, <c>needs_input</c> with its questions.
/// Nothing acts on it yet: the triage lifecycle does.
/// </summary>
internal sealed class TriageVerdictParser : IWorkerVerdictParser
{
    public WorkerRole Role => WorkerRole.Triage;

    public RunVerdict Parse(JsonObject structuredOutput) =>
        TriageVerdict.TryParse(structuredOutput) switch
        {
            TriageVerdict.Ready => RunVerdict.Done(),
            TriageVerdict.NeedsInput needsInput => RunVerdict.NeedsInput(needsInput.Questions),
            _ => RunVerdict.Invalid(),
        };
}
