using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Credentials.Domain.ValueObjects;

namespace Foundry.Modules.Credentials.Infrastructure;

internal sealed class AuthModeJsonConverter : JsonConverter<AuthMode>
{
    private const string TypeProperty = "type";
    private const string ApiKeyType = "api_key";
    private const string OAuthType = "oauth";
    private const string SubscriptionTypeProperty = "subscription_type";

    public override AuthMode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        string type = root.GetProperty(TypeProperty).GetString()
            ?? throw new JsonException("Missing 'type' discriminator.");

        return type switch
        {
            ApiKeyType => ReadApiKey(root),
            OAuthType => ReadOAuth(root),
            _ => throw new JsonException($"Unknown auth mode type: '{type}'."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        AuthMode value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        switch (value)
        {
            case AuthMode.ApiKey:
                writer.WriteString(TypeProperty, ApiKeyType);
                break;

            case AuthMode.OAuth oauth:
                writer.WriteString(TypeProperty, OAuthType);
                if (oauth.SubscriptionType is not null)
                {
                    writer.WriteString(SubscriptionTypeProperty, oauth.SubscriptionType);
                }
                break;
        }

        writer.WriteEndObject();
    }

    private static AuthMode.ApiKey ReadApiKey(JsonElement root)
    {
        // The credential is stored in the separate api_key column and assembled by the entity.
        // Legacy blobs may contain an encrypted_key property — ignore it here; the entity
        // will read NotConfigured from the null api_key column for those rows.
        _ = root;
        return new AuthMode.ApiKey(new ApiKeyCredential.NotConfigured());
    }

    private static AuthMode.OAuth ReadOAuth(JsonElement root)
    {
        // Legacy blobs may contain access_token, refresh_token, expires_at — these are
        // intentionally ignored. System.Text.Json skips unknown properties, so only
        // subscription_type is read from the element.
        string? subscriptionType = root.TryGetProperty(SubscriptionTypeProperty, out JsonElement sub)
            ? sub.GetString()
            : null;
        return new AuthMode.OAuth(subscriptionType);
    }
}
