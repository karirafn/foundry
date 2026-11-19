using System.Text.RegularExpressions;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The structured pull request description an implement verdict proposes (ADR 0100), scrubbed of model-written
/// attribution when it is created, so every consumer (the pull request body and the revision summary comment) only
/// receives scrubbed text. Two rules mirror the Publisher's commit-message strip: every <c>Co-Authored-By</c> trailer,
/// whatever it names, and the Claude Code "Generated with" footer line. They apply to every field: a matching line is
/// removed from <see cref="Cause"/> and <see cref="Notes"/>, and a matching item is removed from a list.
/// </summary>
public sealed partial record PullRequestDescription
{
    // The limits match the implement verdict schema's pullRequest properties.
    public const int MaxChanges = 6;
    public const int MaxVerification = 4;
    public const int MaxAttention = 3;
    public const int ItemMaxLength = 160;
    public const int AttentionItemMaxLength = 200;
    public const int CauseMaxLength = 300;
    public const int NotesMaxLength = 1500;

    private PullRequestDescription(
        string? cause,
        IReadOnlyList<string> changes,
        IReadOnlyList<string> verification,
        IReadOnlyList<string> attention,
        string? notes)
    {
        Cause = cause;
        Changes = changes;
        Verification = verification;
        Attention = attention;
        Notes = notes;
    }

    /// <summary>For a run that fixes something: what failed and why.</summary>
    public string? Cause { get; }

    /// <summary>What the change does, one line each; never empty.</summary>
    public IReadOnlyList<string> Changes { get; }

    /// <summary>What the run ran to verify the change and its result, one line each; never empty.</summary>
    public IReadOnlyList<string> Verification { get; }

    /// <summary>What a reviewer must check before merging, one line each; often empty.</summary>
    public IReadOnlyList<string> Attention { get; }

    /// <summary>Markdown implementation details a reviewer may want.</summary>
    public string? Notes { get; }

    /// <summary>
    /// Returns the scrubbed description, or <see langword="null"/> when <paramref name="draft"/> is missing or breaks a
    /// limit, or when no change or no verification is left once attribution is removed, so the caller falls back to a
    /// body built from the commit subjects. An optional field left empty by the scrub is dropped.
    /// </summary>
    public static PullRequestDescription? TryCreate(RunPullRequestDescription? draft)
    {
        if (draft is null
            || ScrubItems(draft.Changes, MaxChanges, ItemMaxLength) is not { Count: > 0 } changes
            || ScrubItems(draft.Verification, MaxVerification, ItemMaxLength) is not { Count: > 0 } verification
            || ScrubItems(draft.Attention, MaxAttention, AttentionItemMaxLength) is not { } attention
            || !TryScrubText(draft.Cause, CauseMaxLength, out string? cause)
            || !TryScrubText(draft.Notes, NotesMaxLength, out string? notes))
        {
            return null;
        }

        return new PullRequestDescription(cause, changes, verification, attention, notes);
    }

    // Returns null when the list breaks a limit; a blank or attribution item is dropped.
    private static List<string>? ScrubItems(IReadOnlyList<string>? items, int maxItems, int maxLength)
    {
        if (items is null || items.Count > maxItems)
        {
            return null;
        }

        List<string> kept = [];

        foreach (string item in items)
        {
            if (item is null || item.Contains('\n', StringComparison.Ordinal) || item.Contains('\r', StringComparison.Ordinal)
                || RuneCount(item) > maxLength)
            {
                return null;
            }

            string trimmed = item.Trim();
            if (trimmed.Length > 0 && !IsAttribution(trimmed))
            {
                kept.Add(trimmed);
            }
        }

        return kept;
    }

    private static bool TryScrubText(string? text, int maxLength, out string? scrubbed)
    {
        scrubbed = null;

        if (text is null)
        {
            return true;
        }

        if (RuneCount(text) > maxLength)
        {
            return false;
        }

        IEnumerable<string> kept = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(line => !IsAttribution(line));

        // Only surrounding blank lines go: inner trailing spaces can be a Markdown hard line break.
        string withoutLeadingBlankLines = LeadingBlankLines().Replace(string.Join('\n', kept).TrimEnd(), string.Empty);
        scrubbed = withoutLeadingBlankLines.Length == 0 ? null : withoutLeadingBlankLines;
        return true;
    }

    private static bool IsAttribution(string line) => CoAuthoredByTrailer().IsMatch(line) || ClaudeCodeFooter().IsMatch(line);

    private static int RuneCount(string text) => text.EnumerateRunes().Count();

    // Git and GitHub match trailer keys case-insensitively, so this does too.
    [GeneratedRegex(@"^\s*co-authored-by:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CoAuthoredByTrailer();

    [GeneratedRegex(
        @"^\s*(?:🤖\s*)?Generated with \[Claude Code\]\([^)\s]*\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ClaudeCodeFooter();

    [GeneratedRegex(@"^(?:[ \t]*\n)+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LeadingBlankLines();
}
