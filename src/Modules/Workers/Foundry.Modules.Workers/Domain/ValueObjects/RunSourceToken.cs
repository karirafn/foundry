using System.Buffers.Text;
using System.Security.Cryptography;

using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// A Run Source Token (ADR 0112): a random secret a run's worker presents to the Package Source Proxy as its Basic
/// password. Only its <see cref="Hash"/> is persisted; <see cref="ToString"/> never shows the token, so it stays out
/// of logs.
/// </summary>
public sealed class RunSourceToken
{
    /// <summary>The random bytes behind each token.</summary>
    internal const int ByteLength = 32;

    private RunSourceToken(string value)
    {
        Value = value;
        Hash = RunSourceTokenHash.Of(value);
    }

    /// <summary>The token itself, base64url without padding.</summary>
    public string Value { get; }

    public RunSourceTokenHash Hash { get; }

    /// <summary>Mints a token from <see cref="ByteLength"/> bytes of a cryptographic random number generator.</summary>
    public static RunSourceToken Mint() =>
        new(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ByteLength)));

    public override string ToString() => "***";
}
