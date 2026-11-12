using System.Text.Json;
using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Verdicts;

/// <summary>
/// Reads the shared <c>questions</c> definition (<c>workers/roles/shared/verdict.defs.json</c>): an array of at least
/// one <c>{question, context, recommendation}</c> object, with a non-empty question and recommendation and no other
/// properties. Every role whose verdict asks questions reads them here, so there is one way to read them.
/// </summary>
internal static class VerdictQuestionsReader
{
    internal const string QuestionProperty = "question";
    internal const string ContextProperty = "context";
    internal const string RecommendationProperty = "recommendation";

    private const int PropertyCount = 3;

    /// <summary>Returns the questions, or <see langword="null"/> when <paramref name="node"/> breaks the definition.</summary>
    public static IReadOnlyList<RunVerdictQuestion>? TryRead(JsonNode? node)
    {
        if (node is not JsonArray array || array.Count == 0)
        {
            return null;
        }

        List<RunVerdictQuestion> questions = new(array.Count);

        foreach (JsonNode? item in array)
        {
            if (item is not JsonObject entry
                || entry.Count != PropertyCount
                || ReadString(entry[QuestionProperty]) is not { Length: > 0 } question
                || ReadString(entry[ContextProperty]) is not { } context
                || ReadString(entry[RecommendationProperty]) is not { Length: > 0 } recommendation)
            {
                return null;
            }

            questions.Add(new RunVerdictQuestion(question, context, recommendation));
        }

        return questions;
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
