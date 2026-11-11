using System.Text.RegularExpressions;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>A full git object name: 40 lowercase hex digits (SHA-1) or 64 (SHA-256).</summary>
public readonly partial record struct CommitSha
{
    private CommitSha(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static CommitSha From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!Pattern().IsMatch(value))
        {
            throw new ArgumentException("A commit SHA is 40 or 64 lowercase hex digits.", nameof(value));
        }

        return new CommitSha(value);
    }

    public static bool TryFrom(string? value, out CommitSha sha)
    {
        if (value is not null && Pattern().IsMatch(value))
        {
            sha = new CommitSha(value);
            return true;
        }

        sha = default;
        return false;
    }

    public override string ToString() => Value;

    [GeneratedRegex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Pattern();
}
