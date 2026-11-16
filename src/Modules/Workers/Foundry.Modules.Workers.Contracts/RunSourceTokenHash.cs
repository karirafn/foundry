using System.Security.Cryptography;
using System.Text;

namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// The SHA-256 hash of a Run Source Token (ADR 0112), as lowercase hex. A run stores only this, so the token itself
/// never reaches the database; the Package Source Proxy hashes the presented password the same way to find the run.
/// </summary>
public readonly record struct RunSourceTokenHash
{
    private const int HexLength = 64;

    private RunSourceTokenHash(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>The hash of <paramref name="token"/>: the SHA-256 of its UTF-8 bytes.</summary>
    public static RunSourceTokenHash Of(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return new RunSourceTokenHash(Convert.ToHexStringLower(hash));
    }

    /// <summary>Restores a stored hash, which must be 64 lowercase hex characters.</summary>
    public static RunSourceTokenHash FromStored(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length != HexLength || !value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'))
        {
            throw new ArgumentException("A run source token hash is 64 lowercase hex characters.", nameof(value));
        }

        return new RunSourceTokenHash(value);
    }

    public override string ToString() => Value;
}
