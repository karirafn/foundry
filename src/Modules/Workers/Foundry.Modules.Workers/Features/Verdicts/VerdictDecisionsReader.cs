using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Verdicts;

/// <summary>
/// Reads the triage <c>decisions</c> definition (ADR 0123): an array of at most
/// <see cref="TriageResult.MaxDecisions"/> <c>{question, decision, ground, evidence}</c> objects with no other
/// properties, each a valid <see cref="TriageDecision"/>.
/// </summary>
internal static class VerdictDecisionsReader
{
    internal const string QuestionProperty = "question";
    internal const string DecisionProperty = "decision";
    internal const string GroundProperty = "ground";
    internal const string EvidenceProperty = "evidence";

    private const int PropertyCount = 4;

    /// <summary>The <c>ground</c> tokens of the schema and the ground each stands for.</summary>
    internal static readonly IReadOnlyDictionary<string, TriageDecisionGround> GroundTokens =
        new Dictionary<string, TriageDecisionGround>(StringComparer.Ordinal)
        {
            ["settled_business"] = TriageDecisionGround.SettledBusiness,
            ["implementation_detail"] = TriageDecisionGround.ImplementationDetail,
        };

    /// <summary>Returns the decisions, or <see langword="null"/> when <paramref name="node"/> breaks the definition.</summary>
    public static IReadOnlyList<TriageDecision>? TryRead(JsonNode? node)
    {
        if (node is not JsonArray array || array.Count > TriageResult.MaxDecisions)
        {
            return null;
        }

        List<TriageDecision> decisions = new(array.Count);

        foreach (JsonNode? item in array)
        {
            if (TryReadDecision(item) is not { } decision)
            {
                return null;
            }

            decisions.Add(decision);
        }

        return decisions;
    }

    private static TriageDecision? TryReadDecision(JsonNode? item)
    {
        if (item is not JsonObject entry
            || entry.Count != PropertyCount
            || VerdictQuestionsReader.ReadString(entry[QuestionProperty]) is not { } question
            || VerdictQuestionsReader.ReadString(entry[DecisionProperty]) is not { } decision
            || VerdictQuestionsReader.ReadString(entry[GroundProperty]) is not { } groundToken
            || !GroundTokens.TryGetValue(groundToken, out TriageDecisionGround ground)
            || ReadStrings(entry[EvidenceProperty]) is not { } evidence)
        {
            return null;
        }

        return TriageDecision.Create(question, decision, ground, evidence) is Result<TriageDecision>.Success success
            ? success.Value
            : null;
    }

    private static List<string>? ReadStrings(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return null;
        }

        List<string> values = new(array.Count);

        foreach (JsonNode? item in array)
        {
            if (VerdictQuestionsReader.ReadString(item) is not { } value)
            {
                return null;
            }

            values.Add(value);
        }

        return values;
    }
}
