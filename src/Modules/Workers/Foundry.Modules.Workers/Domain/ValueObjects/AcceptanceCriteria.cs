using System.Text;
using System.Text.RegularExpressions;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// Extracts an issue's numbered acceptance criteria from its Markdown body (ADR 0114), so a review answers each by id.
/// </summary>
public static partial class AcceptanceCriteria
{
    private const string HeadingText = "acceptance criteria";

    /// <summary>
    /// Finds the first heading, at any level, whose text is <c>Acceptance Criteria</c> (any case, an optional trailing
    /// colon), takes the top-level list items up to the next heading of the same or a higher level, strips a leading
    /// <c>[ ]</c> or <c>[x]</c> checkbox, and numbers them <c>AC1</c>…<c>ACn</c>. A continuation line, including a
    /// nested list item, joins its item's text. A body without the heading yields no criteria.
    /// </summary>
    public static IReadOnlyList<AcceptanceCriterion> Extract(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return [];
        }

        string[] lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        List<string> items = [];
        StringBuilder? current = null;
        int? sectionLevel = null;
        int? itemIndent = null;
        bool previousBlank = false;
        bool inFence = false;

        foreach (string line in lines)
        {
            if (FencePattern().IsMatch(line))
            {
                inFence = !inFence;
            }

            Match heading = inFence ? Match.Empty : HeadingPattern().Match(line);

            if (sectionLevel is null)
            {
                if (heading.Success && IsCriteriaHeading(heading.Groups["text"].Value))
                {
                    sectionLevel = heading.Groups["hashes"].Length;
                }

                continue;
            }

            if (heading.Success && heading.Groups["hashes"].Length <= sectionLevel)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                previousBlank = true;
                continue;
            }

            Match item = ListItemPattern().Match(line);
            int indent = item.Success ? item.Groups["indent"].Length : 0;

            if (item.Success && (itemIndent is null || indent <= itemIndent))
            {
                Flush(items, current);
                itemIndent ??= indent;
                current = new StringBuilder(StripCheckbox(item.Groups["text"].Value.Trim()));
            }
            else if (current is not null && !(previousBlank && !char.IsWhiteSpace(line[0])))
            {
                string continuation = item.Success ? StripCheckbox(item.Groups["text"].Value.Trim()) : line.Trim();
                current.Append(' ').Append(continuation);
            }
            else
            {
                Flush(items, current);
                current = null;
            }

            previousBlank = false;
        }

        Flush(items, current);

        return [.. items.Select((text, index) => new AcceptanceCriterion(index + 1, text))];
    }

    private static void Flush(List<string> items, StringBuilder? current)
    {
        string? text = current?.ToString().Trim();

        if (!string.IsNullOrEmpty(text))
        {
            items.Add(text);
        }
    }

    private static bool IsCriteriaHeading(string text) =>
        string.Equals(text.Trim().TrimEnd(':').Trim(), HeadingText, StringComparison.OrdinalIgnoreCase);

    private static string StripCheckbox(string text)
    {
        Match checkbox = CheckboxPattern().Match(text);
        return checkbox.Success ? text[checkbox.Length..].TrimStart() : text;
    }

    [GeneratedRegex(@"^ {0,3}(?<hashes>#{1,6})(?:[ \t]+(?<text>.*?))?(?:[ \t]+#+)?[ \t]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?:[-*+]|\d{1,9}[.)])(?:[ \t]+(?<text>.*))?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(@"^\[[ xX]\](?:[ \t]+|$)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CheckboxPattern();

    [GeneratedRegex(@"^ {0,3}(?:```|~~~)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FencePattern();
}
