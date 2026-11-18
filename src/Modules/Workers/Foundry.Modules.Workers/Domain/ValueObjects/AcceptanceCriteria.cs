using System.Globalization;
using System.Text.RegularExpressions;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>One numbered acceptance criterion of an issue: <c>AC1</c>, <c>AC2</c>, … in the order the issue lists them.</summary>
public sealed record AcceptanceCriterion
{
    public const string IdPrefix = "AC";

    public AcceptanceCriterion(string id, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(text);

        if (!IdPattern().IsMatch(id))
        {
            throw new ArgumentException("A criterion id is AC followed by its number.", nameof(id));
        }

        Id = id;
        Text = text;
    }

    public string Id { get; }

    public string Text { get; }

    public static string IdFor(int number) => IdPrefix + number.ToString(CultureInfo.InvariantCulture);

    private static Regex IdPattern() => AcceptanceCriteria.CriterionIdPattern();
}

/// <summary>
/// The acceptance criteria code read from an issue body (ADR 0114): the list items under the first heading whose text
/// names <c>acceptance criteria</c>, numbered in order. A body without such a heading has none.
/// </summary>
public sealed partial record AcceptanceCriteria
{
    private const string HeadingText = "acceptance criteria";

    private AcceptanceCriteria(IReadOnlyList<AcceptanceCriterion> items)
    {
        Items = items;
    }

    public static AcceptanceCriteria None { get; } = new([]);

    public IReadOnlyList<AcceptanceCriterion> Items { get; }

    /// <summary>Rehydrates criteria recorded on a run; their ids must be <c>AC1…ACn</c> in order.</summary>
    public static AcceptanceCriteria FromItems(IReadOnlyList<AcceptanceCriterion> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        for (int index = 0; index < items.Count; index++)
        {
            if (items[index].Id != AcceptanceCriterion.IdFor(index + 1))
            {
                throw new ArgumentException("Criteria are numbered AC1 to ACn in order.", nameof(items));
            }
        }

        return new AcceptanceCriteria([.. items]);
    }

    /// <summary>
    /// Takes the list items under the first heading (any level) whose text contains <c>acceptance criteria</c>, up to
    /// the next heading of the same or a higher level. A <c>[ ]</c> or <c>[x]</c> checkbox is stripped; continuation
    /// lines, nested items included, join their item's text.
    /// </summary>
    public static AcceptanceCriteria Extract(string issueBody)
    {
        ArgumentNullException.ThrowIfNull(issueBody);

        string[] lines = issueBody.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int? sectionLevel = null;
        bool inFence = false;
        bool afterBlank = false;
        int itemIndent = -1;
        List<List<string>> items = [];

        foreach (string line in lines)
        {
            if (FencePattern().IsMatch(line))
            {
                inFence = !inFence;
            }

            Match heading = inFence ? Match.Empty : HeadingPattern().Match(line);

            if (heading.Success)
            {
                int level = heading.Groups["hashes"].Value.Length;

                if (sectionLevel is { } open && level <= open)
                {
                    break;
                }

                if (sectionLevel is null
                    && heading.Groups["text"].Value.Contains(HeadingText, StringComparison.OrdinalIgnoreCase))
                {
                    sectionLevel = level;
                }

                afterBlank = false;
                continue;
            }

            if (sectionLevel is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                afterBlank = true;
                continue;
            }

            int indent = line.Length - line.TrimStart().Length;
            Match listItem = ListItemPattern().Match(line);

            if (listItem.Success && (itemIndent < 0 || indent <= itemIndent))
            {
                itemIndent = indent;
                items.Add([listItem.Groups["text"].Value.Trim()]);
            }
            else if (items.Count > 0 && (!afterBlank || indent > itemIndent))
            {
                items[^1].Add(line.Trim());
            }
            else if (items.Count > 0)
            {
                // A paragraph after the list ends it: later lines join no item.
                break;
            }

            afterBlank = false;
        }

        return new AcceptanceCriteria(
            [.. items.Select((parts, index) =>
                new AcceptanceCriterion(AcceptanceCriterion.IdFor(index + 1), string.Join(' ', parts.Where(part => part.Length > 0))))]);
    }

    [GeneratedRegex(@"^AC[1-9][0-9]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    internal static partial Regex CriterionIdPattern();

    [GeneratedRegex(@"^ {0,3}(?<hashes>#{1,6})(?:[ \t]+(?<text>.*?))?[ \t#]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^ {0,3}(?:```|~~~)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FencePattern();

    [GeneratedRegex(@"^\s*(?:[-*+]|[0-9]{1,9}[.)])[ \t]+(?:\[[ xX]\][ \t]+)?(?<text>.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ListItemPattern();

    public bool Equals(AcceptanceCriteria? other) => other is not null && Items.SequenceEqual(other.Items);

    public override int GetHashCode() => Items.Aggregate(0, (hash, item) => HashCode.Combine(hash, item));
}
