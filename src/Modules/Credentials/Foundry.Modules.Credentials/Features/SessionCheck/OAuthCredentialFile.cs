using System.Text.Json;

namespace Foundry.Modules.Credentials.Features.SessionCheck;

/// <summary>
/// Classifies the Claude Code CLI's <c>.credentials.json</c>. The relevant shape is
/// <c>{"claudeAiOauth":{"accessToken":"…","refreshToken":"…","expiresAt":&lt;epoch ms&gt;,…}}</c>.
/// Only the presence of the tokens and the expiry are inspected; token values never leave this method.
/// </summary>
internal static class OAuthCredentialFile
{
    internal const string FileName = ".credentials.json";

    private const string OAuthPropertyName = "claudeAiOauth";
    private const string RefreshTokenPropertyName = "refreshToken";
    private const string ExpiresAtPropertyName = "expiresAt";

    public static OAuthSessionStatus Classify(string? json, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new OAuthSessionStatus.SignedOut();
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return new OAuthSessionStatus.Unreadable();
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new OAuthSessionStatus.Unreadable();
            }

            if (!root.TryGetProperty(OAuthPropertyName, out JsonElement oauth)
                || oauth.ValueKind == JsonValueKind.Null)
            {
                return new OAuthSessionStatus.SignedOut();
            }

            if (oauth.ValueKind != JsonValueKind.Object)
            {
                return new OAuthSessionStatus.Unreadable();
            }

            bool hasRefreshToken = false;

            if (oauth.TryGetProperty(RefreshTokenPropertyName, out JsonElement refreshToken)
                && refreshToken.ValueKind == JsonValueKind.String)
            {
                // An empty string is the CLI's dead-token marker after an invalid_grant response.
                if (refreshToken.GetString()!.Length == 0)
                {
                    return new OAuthSessionStatus.RefreshRejected();
                }

                hasRefreshToken = true;
            }

            if (!oauth.TryGetProperty(ExpiresAtPropertyName, out JsonElement expiresAt)
                || expiresAt.ValueKind == JsonValueKind.Null)
            {
                // A token without an expiry (e.g. a long-lived inference token) never expires.
                return new OAuthSessionStatus.Active();
            }

            if (expiresAt.ValueKind != JsonValueKind.Number || !expiresAt.TryGetDouble(out double expiresAtMs))
            {
                return new OAuthSessionStatus.Unreadable();
            }

            if (expiresAtMs > now.ToUnixTimeMilliseconds())
            {
                return new OAuthSessionStatus.Active();
            }

            return hasRefreshToken
                ? new OAuthSessionStatus.Expired()
                : new OAuthSessionStatus.NotRefreshable();
        }
    }
}
