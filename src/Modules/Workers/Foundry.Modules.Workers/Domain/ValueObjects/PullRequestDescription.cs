using System.Text.RegularExpressions;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The pull request description an implement verdict proposes (ADR 0100), scrubbed of model-written attribution when
/// it is created, so every consumer (the pull request body and the revision summary comment) only receives scrubbed
/// text. Two rules mirror the Publisher's commit-message strip: every <c>Co-Authored-By</c> trailer, whatever it names,
/// and the Claude Code "Generated with" footer line. Every other line is kept.
/// </summary>
public sealed partial record PullRequestDescription
{
    /// <summary>The longest description kept, matching the verdict schema's <c>maxLength</c>.</summary>
    public const int MaxLength = 20_000;

    private PullRequestDescription(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>
    /// Returns the scrubbed description, or <see langword="null"/> when <paramref name="body"/> is missing, too long, or
    /// empty once attribution is removed, so the caller falls back to a body built from the commit subjects.
    /// </summary>
    public static PullRequestDescription? TryCreate(string? body)
    {
        if (body is null || body.EnumerateRunes().Count() > MaxLength)
        {
            return null;
        }

        IEnumerable<string> kept = body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(line => !CoAuthoredByTrailer().IsMatch(line) && !ClaudeCodeFooter().IsMatch(line));

        // Only surrounding blank lines go: inner trailing spaces can be a Markdown hard line break.
        string scrubbed = string.Join('\n', kept).TrimEnd();
        string withoutLeadingBlankLines = LeadingBlankLines().Replace(scrubbed, string.Empty);

        return withoutLeadingBlankLines.Length == 0 ? null : new PullRequestDescription(withoutLeadingBlankLines);
    }

    public override string ToString() => Value;

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
