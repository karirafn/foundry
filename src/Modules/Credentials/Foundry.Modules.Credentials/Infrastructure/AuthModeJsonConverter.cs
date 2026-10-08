using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Credentials.Domain.ValueObjects;

namespace Foundry.Modules.Credentials.Infrastructure;

internal sealed class AuthModeJsonConverter : JsonConverter<AuthMode>
{
    private const string TypeProperty = "type";
    private const string ApiKeyType = "api_key";
    private const string OAuthType = "oauth";
    private const string KeyProperty = "encrypted_key";
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
            // TODO(step-5): rework to write the full ApiKeyCredential variant; shim keeps build green.
            case AuthMode.ApiKey apiKey:
                writer.WriteString(TypeProperty, ApiKeyType);
                if (apiKey.Credential is ApiKeyCredential.Present p)
                {
                    writer.WriteString(KeyProperty, p.Value);
                }
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
        // TODO(step-5): rework to read full ApiKeyCredential variant; shim keeps build green.
        if (!root.TryGetProperty(KeyProperty, out JsonElement keyElement))
        {
            return new AuthMode.ApiKey(new ApiKeyCredential.NotConfigured());
        }

        string? key = keyElement.GetString();
        ApiKeyCredential credential = string.IsNullOrEmpty(key)
            ? new ApiKeyCredential.NotConfigured()
            : new ApiKeyCredential.Present(key);
        return new AuthMode.ApiKey(credential);
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
