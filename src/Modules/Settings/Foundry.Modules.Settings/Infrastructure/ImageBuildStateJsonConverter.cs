using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Settings.Domain.ValueObjects;

namespace Foundry.Modules.Settings.Infrastructure;

internal sealed class ImageBuildStateJsonConverter : JsonConverter<ImageBuildState>
{
    private const string TypeProperty = "type";
    private const string ErrorTailProperty = "error_tail";
    private const string IdleType = "idle";
    private const string BuildingType = "building";
    private const string FailedType = "failed";

    public override ImageBuildState Read(
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
            IdleType => new ImageBuildState.Idle(),
            BuildingType => new ImageBuildState.Building(),
            FailedType => ReadFailed(root),
            _ => throw new JsonException($"Unknown image build state type: '{type}'."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        ImageBuildState value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        switch (value)
        {
            case ImageBuildState.Idle:
                writer.WriteString(TypeProperty, IdleType);
                break;

            case ImageBuildState.Building:
                writer.WriteString(TypeProperty, BuildingType);
                break;

            case ImageBuildState.Failed failed:
                writer.WriteString(TypeProperty, FailedType);
                writer.WriteString(ErrorTailProperty, failed.ErrorTail);
                break;

            default:
                throw new JsonException($"Unhandled ImageBuildState type: '{value.GetType().Name}'.");
        }

        writer.WriteEndObject();
    }

    private static ImageBuildState.Failed ReadFailed(JsonElement root)
    {
        string? errorTail = root.TryGetProperty(ErrorTailProperty, out JsonElement el)
            ? el.GetString()
            : null;
        return new ImageBuildState.Failed(errorTail);
    }
}
