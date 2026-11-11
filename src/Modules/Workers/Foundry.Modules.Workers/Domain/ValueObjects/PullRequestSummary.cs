namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The one-line summary an implement verdict proposes for the pull request title (ADR 0100): trimmed, a single line,
/// and at most <see cref="MaxLength"/> characters.
/// </summary>
public sealed record PullRequestSummary
{
    /// <summary>The longest summary kept, counted in Unicode code points as the verdict schema's <c>maxLength</c> is.</summary>
    public const int MaxLength = 72;

    private PullRequestSummary(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>
    /// Returns the summary, or <see langword="null"/> when <paramref name="text"/> is blank, spans lines or is too
    /// long, so the caller falls back to the issue title.
    /// </summary>
    public static PullRequestSummary? TryCreate(string? text)
    {
        string trimmed = text?.Trim() ?? string.Empty;

        if (trimmed.Length == 0
            || trimmed.AsSpan().IndexOfAny('\r', '\n') >= 0
            || trimmed.EnumerateRunes().Count() > MaxLength)
        {
            return null;
        }

        return new PullRequestSummary(trimmed);
    }

    public override string ToString() => Value;
}
