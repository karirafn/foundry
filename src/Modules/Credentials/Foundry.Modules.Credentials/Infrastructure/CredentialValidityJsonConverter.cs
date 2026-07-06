using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Credentials.Domain.ValueObjects;

namespace Foundry.Modules.Credentials.Infrastructure;

internal sealed class CredentialValidityJsonConverter : JsonConverter<CredentialValidity>
{
    private const string TypeProperty = "type";
    private const string ValidType = "valid";
    private const string InvalidType = "invalid";
    private const string ReasonProperty = "reason";

    public override CredentialValidity Read(
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
            ValidType => new CredentialValidity.Valid(),
            InvalidType => ReadInvalid(root),
            _ => throw new JsonException($"Unknown credential validity type: '{type}'."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        CredentialValidity value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        switch (value)
        {
            case CredentialValidity.Valid:
                writer.WriteString(TypeProperty, ValidType);
                break;

            case CredentialValidity.Invalid invalid:
                writer.WriteString(TypeProperty, InvalidType);
                writer.WriteString(ReasonProperty, invalid.Reason);
                break;

            default:
                // Sealed hierarchy with private constructor — unreachable.
                throw new InvalidOperationException($"Unexpected CredentialValidity type: {value.GetType().Name}.");
        }

        writer.WriteEndObject();
    }

    private static CredentialValidity.Invalid ReadInvalid(JsonElement root)
    {
        string reason = root.GetProperty(ReasonProperty).GetString()
            ?? string.Empty;
        return new CredentialValidity.Invalid(reason);
    }
}
