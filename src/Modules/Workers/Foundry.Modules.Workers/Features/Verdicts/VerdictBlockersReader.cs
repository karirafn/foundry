using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Features.Verdicts;

/// <summary>
/// Reads the triage <c>blockedBy</c> definition: an array of at least one <c>{issue, reason}</c> object, with an
/// integer issue number and a reason that is non-blank once its format characters are removed and its whitespace and
/// control characters collapse to single spaces, and no other properties. The blocker carries the normalized reason.
/// </summary>
internal static partial class VerdictBlockersReader
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
                || ReadReason(entry[ReasonProperty]) is not { } reason)
            {
                return null;
            }

            blockers.Add(new TriageBlocker(issue, reason));
        }

        return blockers;
    }

    // The reason is posted publicly inside a code span, where a blank line or control character would break the
    // span, and a format character (bidi override or isolate, zero-width or tag character) would reorder the line
    // or render it blank. It is reduced to one trimmed line without format characters before anything else sees it.
    private static string? ReadReason(JsonNode? node) =>
        VerdictQuestionsReader.ReadString(node) is { } text
        && NonSpaceRun().Replace(WithoutFormatCharacters(text), " ").Trim() is { Length: > 0 } normalized
            ? normalized
            : null;

    // Runes rather than chars, because the tag block U+E0000-E007F is a surrogate pair in UTF-16.
    private static string WithoutFormatCharacters(string text)
    {
        StringBuilder builder = new(text.Length);

        foreach (Rune rune in text.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format)
            {
                builder.Append(rune.ToString());
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"[\s\p{Cc}]+")]
    private static partial Regex NonSpaceRun();

    private static int? ReadInteger(JsonNode? node) =>
        node is JsonValue value
        && value.GetValueKind() == JsonValueKind.Number
        && value.TryGetValue(out int number)
            ? number
            : null;
}
