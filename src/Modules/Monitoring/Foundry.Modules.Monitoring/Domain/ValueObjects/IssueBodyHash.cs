using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// The fingerprint of an issue body the Issue Watch compares against (ADR 0096): the lowercase hex SHA-256 of the
/// UTF-8 body after CRLF and CR become LF and trailing whitespace is trimmed, so two bodies that differ only in line
/// endings or trailing whitespace hash equal. Only <see cref="Of"/> and <see cref="OfSpec"/> make one from a body.
/// </summary>
public sealed partial record IssueBodyHash
{
    private IssueBodyHash(string value)
    {
        Value = value;
    }

    /// <summary>The 64 lowercase hex characters of the hash.</summary>
    public string Value { get; }

    public static IssueBodyHash Of(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Hash(Normalize(body));
    }

    /// <summary>
    /// The fingerprint of the spec an issue body states (the Triaged Spec Hash): as <see cref="Of"/>, after every
    /// task-list marker at a list item's start becomes <c>[ ]</c>, so ticking or unticking an acceptance criterion's
    /// checkbox is not a rewrite. A marker inside a fenced code block is normalized too: a fingerprint does not need a
    /// markdown parser to tell fences apart.
    /// </summary>
    public static IssueBodyHash OfSpec(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Hash(TickedTaskListMarker().Replace(Normalize(body), "${item}[ ]"));
    }

    /// <summary>Restores a hash <see cref="Of"/> or <see cref="OfSpec"/> computed and the database stored.</summary>
    /// <exception cref="FormatException"><paramref name="stored"/> is not 64 lowercase hex characters.</exception>
    internal static IssueBodyHash FromStored(string stored) =>
        stored is not null && StoredForm().IsMatch(stored)
            ? new IssueBodyHash(stored)
            : throw new FormatException("A stored issue body hash is 64 lowercase hex characters.");

    public override string ToString() => Value;

    private static string Normalize(string body) =>
        body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd();

    private static IssueBodyHash Hash(string normalized) =>
        new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))));

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex StoredForm();

    // A list item's start: indentation, a bullet (-, * or +) or an ordered marker (1. or 1)), then whitespace.
    [GeneratedRegex(
        @"^(?<item>[ \t]*(?:[-*+]|\d{1,9}[.)])[ \t]+)\[[xX]\]",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TickedTaskListMarker();
}
