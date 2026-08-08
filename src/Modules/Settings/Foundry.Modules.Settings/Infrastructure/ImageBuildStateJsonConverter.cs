using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Settings.Domain.ValueObjects;

namespace Foundry.Modules.Settings.Infrastructure;

internal sealed class ImageBuildStateJsonConverter : JsonConverter<ImageBuildState>
{
    private const string TypeProperty = "type";
    private const string ErrorTailProperty = "error_tail";
    private const string NextRetryAtProperty = "next_retry_at";
    private const string AttemptProperty = "attempt";
    private const string IdleType = "idle";
    private const string BuildingType = "building";
    private const string FailedType = "failed";

    // Clamp deserialized attempt values to prevent overflow in exponential backoff computation.
    // Beyond this point the backoff is always at maximum, so higher values add no information.
    private const int MaxAttempt = 100;

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
                if (failed.NextRetryAt is DateTimeOffset nextRetryAt)
                {
                    writer.WriteString(NextRetryAtProperty, nextRetryAt);
                }
                else
                {
                    writer.WriteNull(NextRetryAtProperty);
                }
                writer.WriteNumber(AttemptProperty, failed.Attempt);
                break;

            default:
                throw new JsonException($"Unhandled ImageBuildState type: '{value.GetType().Name}'.");
        }

        writer.WriteEndObject();
    }

    private static ImageBuildState.Failed ReadFailed(JsonElement root)
    {
        string? errorTail = root.TryGetProperty(ErrorTailProperty, out JsonElement errorEl)
            ? errorEl.GetString()
            : null;

        DateTimeOffset? nextRetryAt = root.TryGetProperty(NextRetryAtProperty, out JsonElement retryEl)
            && retryEl.ValueKind != JsonValueKind.Null
            ? retryEl.GetDateTimeOffset()
            : null;

        int rawAttempt = root.TryGetProperty(AttemptProperty, out JsonElement attemptEl)
            ? attemptEl.GetInt32()
            : 0;

        // Clamp defensively: a tampered or corrupted row must not cause overflow in backoff computation.
        int attempt = Math.Clamp(rawAttempt, 0, MaxAttempt);

        return new ImageBuildState.Failed(errorTail, nextRetryAt, attempt);
    }
}
