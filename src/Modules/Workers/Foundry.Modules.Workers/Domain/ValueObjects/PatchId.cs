using System.Text.RegularExpressions;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The stable <c>git patch-id</c> of a reviewed commit's zero-context diff (ADR 0114): equal for two commits that make
/// the same change, so a clean rebase keeps it. 40 lowercase hex digits (SHA-1) or 64 (SHA-256).
/// </summary>
public readonly partial record struct PatchId
{
    private PatchId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static PatchId From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!Pattern().IsMatch(value))
        {
            throw new ArgumentException("A patch-id is 40 or 64 lowercase hex digits.", nameof(value));
        }

        return new PatchId(value);
    }

    public static bool TryFrom(string? value, out PatchId patchId)
    {
        if (value is not null && Pattern().IsMatch(value))
        {
            patchId = new PatchId(value);
            return true;
        }

        patchId = default;
        return false;
    }

    public override string ToString() => Value;

    [GeneratedRegex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Pattern();
}
