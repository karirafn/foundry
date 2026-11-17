using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// The fingerprint of an issue body the Issue Watch compares against (ADR 0096): the lowercase hex SHA-256 of the
/// UTF-8 body after CRLF and CR become LF and trailing whitespace is trimmed, so two bodies that differ only in line
/// endings or trailing whitespace hash equal. Only <see cref="Of"/> makes one from a body.
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

        string normalized = body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd();

        return new IssueBodyHash(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))));
    }

    /// <summary>Restores a hash <see cref="Of"/> computed and the database stored.</summary>
    /// <exception cref="FormatException"><paramref name="stored"/> is not 64 lowercase hex characters.</exception>
    internal static IssueBodyHash FromStored(string stored) =>
        stored is not null && StoredForm().IsMatch(stored)
            ? new IssueBodyHash(stored)
            : throw new FormatException("A stored issue body hash is 64 lowercase hex characters.");

    public override string ToString() => Value;

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex StoredForm();
}
