using System.Security.Cryptography;
using System.Text;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Verifies GitHub's <c>X-Hub-Signature-256</c> header: <c>sha256=</c> followed by the lower-case hex
/// HMAC-SHA256 of the raw request body under the repository's webhook secret. The comparison is
/// constant-time, and runs before the body is parsed.
/// </summary>
internal static class GitHubWebhookSignature
{
    internal const string HeaderName = "X-Hub-Signature-256";

    private const string Prefix = "sha256=";
    private const int HmacSha256Length = 32;

    public static bool IsValid(ReadOnlySpan<byte> body, string? signatureHeader, string? secret)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signatureHeader))
        {
            return false;
        }

        if (!signatureHeader.StartsWith(Prefix, StringComparison.Ordinal)
            || signatureHeader.Length != Prefix.Length + (HmacSha256Length * 2))
        {
            return false;
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(signatureHeader.AsSpan(Prefix.Length));
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    /// <summary>Computes the header value GitHub would send for <paramref name="body"/> — used by tests.</summary>
    internal static string Compute(ReadOnlySpan<byte> body, string secret)
        => Prefix + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
}
