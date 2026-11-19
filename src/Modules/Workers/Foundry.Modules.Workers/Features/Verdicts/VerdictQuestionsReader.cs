using System.Text.Json;
using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Verdicts;

/// <summary>
/// Reads the shared <c>questions</c> definition (<c>workers/roles/shared/verdict.defs.json</c>): an array of at least
/// one structured question with no other properties. Every role whose verdict asks questions reads them here, so
/// there is one way to read them.
/// </summary>
internal static class VerdictQuestionsReader
{
    internal const string QuestionProperty = "question";
    internal const string ProblemProperty = "problem";
    internal const string ImpactProperty = "impact";
    internal const string OptionsProperty = "options";
    internal const string RecommendationProperty = "recommendation";
    internal const string EvidenceProperty = "evidence";
    internal const string RecommendedOptionProperty = "option";
    internal const string ReasonProperty = "reason";

    private const int PropertyCount = 6;
    private const int RecommendationPropertyCount = 2;

    /// <summary>
    /// Returns the questions, or <see langword="null"/> when <paramref name="node"/> breaks the definition. A
    /// recommended letter beyond the option count breaks it too: the schema cannot compare two fields.
    /// </summary>
    public static IReadOnlyList<InputQuestion.Structured>? TryRead(JsonNode? node)
    {
        if (node is not JsonArray array || array.Count == 0)
        {
            return null;
        }

        List<InputQuestion.Structured> questions = new(array.Count);

        foreach (JsonNode? item in array)
        {
            if (TryReadQuestion(item) is not { } question)
            {
                return null;
            }

            questions.Add(question);
        }

        return questions;
    }

    private static InputQuestion.Structured? TryReadQuestion(JsonNode? item)
    {
        if (item is not JsonObject entry
            || entry.Count != PropertyCount
            || ReadString(entry[QuestionProperty]) is not { } question
            || ReadString(entry[ProblemProperty]) is not { } problem
            || ReadString(entry[ImpactProperty]) is not { } impact
            || ReadStrings(entry[OptionsProperty]) is not { } options
            || ReadRecommendation(entry[RecommendationProperty]) is not { } recommendation
            || ReadStrings(entry[EvidenceProperty]) is not { } evidence)
        {
            return null;
        }

        return InputQuestion.Structured.Create(question, problem, impact, options, recommendation.Option, recommendation.Reason, evidence)
            is Result<InputQuestion.Structured>.Success success
            ? success.Value
            : null;
    }

    private static (string Option, string Reason)? ReadRecommendation(JsonNode? node)
    {
        if (node is not JsonObject recommendation
            || recommendation.Count != RecommendationPropertyCount
            || ReadString(recommendation[RecommendedOptionProperty]) is not { } option
            || ReadString(recommendation[ReasonProperty]) is not { } reason)
        {
            return null;
        }

        return (option, reason);
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
            if (ReadString(item) is not { } value)
            {
                return null;
            }

            values.Add(value);
        }

        return values;
    }

    // A JSON string escaping a lone surrogate (such as "\ud800") parses, but reading it as text throws, so it reads
    // as not-a-string and the verdict fails the schema instead of faulting the run.
    internal static string? ReadString(JsonNode? node)
    {
        if (node is null || node.GetValueKind() != JsonValueKind.String)
        {
            return null;
        }

        try
        {
            return node.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
