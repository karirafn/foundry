using System.Text.Json.Serialization;

namespace Foundry.Modules.Workers.Features.ImageBuild;

/// <summary>
/// Serialized into <see cref="Foundry.Shared.SystemNotification.Message"/> for image-build broadcasts.
/// Field names must exactly match the settings DTO shape that the frontend reads — a mismatch fails silently.
/// </summary>
internal sealed record ImageBuildStatusNotification(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("logTail")] string? LogTail,
    [property: JsonPropertyName("nextRetryAt")] DateTimeOffset? NextRetryAt,
    [property: JsonPropertyName("attempt")] int Attempt);
