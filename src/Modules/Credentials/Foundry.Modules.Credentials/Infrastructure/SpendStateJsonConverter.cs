using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Credentials.Domain.ValueObjects;

namespace Foundry.Modules.Credentials.Infrastructure;

internal sealed class SpendStateJsonConverter : JsonConverter<SpendState>
{
    private const string TypeProperty = "type";
    private const string AvailableType = "available";
    private const string BlockedType = "blocked";
    private const string NextProbeAtProperty = "next_probe_at";

    public override SpendState Read(
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
            AvailableType => new SpendState.Available(),
            BlockedType => ReadBlocked(root),
            _ => throw new JsonException($"Unknown spend state type: '{type}'."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        SpendState value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        switch (value)
        {
            case SpendState.Available:
                writer.WriteString(TypeProperty, AvailableType);
                break;

            case SpendState.Blocked blocked:
                writer.WriteString(TypeProperty, BlockedType);
                writer.WriteString(NextProbeAtProperty, blocked.NextProbeAt.ToString("O"));
                break;

            default:
                // Sealed hierarchy with private constructor — unreachable.
                throw new InvalidOperationException($"Unexpected SpendState type: {value.GetType().Name}.");
        }

        writer.WriteEndObject();
    }

    private static SpendState.Blocked ReadBlocked(JsonElement root)
    {
        string nextProbeAtRaw = root.GetProperty(NextProbeAtProperty).GetString()
            ?? throw new JsonException("Missing 'next_probe_at' field on blocked spend state.");
        DateTimeOffset nextProbeAt = DateTimeOffset.Parse(nextProbeAtRaw, null, DateTimeStyles.RoundtripKind);
        return new SpendState.Blocked(nextProbeAt);
    }
}
