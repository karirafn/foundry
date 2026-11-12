using System.Text.Json;
using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Features.Verdicts;

/// <summary>
/// Reads the triage <c>blockedBy</c> definition: an array of at least one <c>{issue, reason}</c> object, with an
/// integer issue number and a non-empty reason and no other properties.
/// </summary>
internal static class VerdictBlockersReader
{
    internal const string IssueProperty = "issue";
    internal const string ReasonProperty = "reason";

    private const int PropertyCount = 2;

    /// <summary>Returns the blockers, or <see langword="null"/> when <paramref name="node"/> breaks the definition.</summary>
    public static IReadOnlyList<TriageBlocker>? TryRead(JsonNode? node)
    {
        if (node is not JsonArray array || array.Count == 0)
        {
            return null;
        }

        List<TriageBlocker> blockers = new(array.Count);

        foreach (JsonNode? item in array)
        {
            if (item is not JsonObject entry
                || entry.Count != PropertyCount
                || ReadInteger(entry[IssueProperty]) is not { } issue
                || VerdictQuestionsReader.ReadString(entry[ReasonProperty]) is not { Length: > 0 } reason)
            {
                return null;
            }

            blockers.Add(new TriageBlocker(issue, reason));
        }

        return blockers;
    }

    private static int? ReadInteger(JsonNode? node) =>
        node is JsonValue value
        && value.GetValueKind() == JsonValueKind.Number
        && value.TryGetValue(out int number)
            ? number
            : null;
}
