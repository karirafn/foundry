using System.Security.Cryptography;
using System.Text;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Verifies GitLab's <c>X-Gitlab-Token</c> header: the plain secret token configured on the hook, which
/// must equal the repository's webhook secret. Both values are hashed to a fixed length before a
/// constant-time comparison, so neither their content nor their length leaks through timing. The check
/// runs before the body is parsed.
/// </summary>
internal static class GitLabWebhookToken
{
    internal const string HeaderName = "X-Gitlab-Token";

    public static bool IsValid(string? tokenHeader, string? secret)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(tokenHeader))
        {
            return false;
        }

        byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        byte[] provided = SHA256.HashData(Encoding.UTF8.GetBytes(tokenHeader));
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }
}
