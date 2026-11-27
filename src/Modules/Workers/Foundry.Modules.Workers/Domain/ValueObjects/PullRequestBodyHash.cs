using System.Security.Cryptography;
using System.Text;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The SHA-256, as 64 lowercase hex characters, of a pull request body Foundry wrote (ADR 0100). A later run replaces
/// the body only while the live body still hashes to it, so a person's edit is never overwritten.
/// </summary>
public sealed record PullRequestBodyHash
{
    private const int HexLength = 64;

    private PullRequestBodyHash(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>
    /// The hash of <paramref name="body"/> with every <c>\r\n</c> read as <c>\n</c> and trailing whitespace trimmed,
    /// because a provider may hand a body back with either changed; any other edit changes the hash.
    /// </summary>
    public static PullRequestBodyHash Hash(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        string normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return new PullRequestBodyHash(Convert.ToHexStringLower(hash));
    }

    /// <exception cref="ArgumentException"><paramref name="value"/> is not 64 lowercase hex characters.</exception>
    public static PullRequestBodyHash FromStored(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length != HexLength || !value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'))
        {
            throw new ArgumentException("A pull request body hash is 64 lowercase hex characters.", nameof(value));
        }

        return new PullRequestBodyHash(value);
    }

    public override string ToString() => Value;
}
