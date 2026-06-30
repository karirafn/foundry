using System.Text.Json.Serialization;

namespace Foundry.Modules.Workers.Domain;

public sealed record CommitMarker
{
    public const int ShaMaxLength = 40;
    public const int MessageMaxLength = 500;

    public DateTimeOffset ObservedAt { get; }

    public string Sha { get; }

    public string Message { get; }

    [JsonConstructor]
    private CommitMarker(DateTimeOffset observedAt, string sha, string message)
    {
        ObservedAt = observedAt;
        Sha = sha;
        Message = message;
    }

    /// <summary>
    /// Creates a new <see cref="CommitMarker"/> from ingested provider data.
    /// Validates that <paramref name="sha"/> and <paramref name="message"/> are non-empty,
    /// and truncates each to its maximum length.
    /// </summary>
    public static CommitMarker Create(DateTimeOffset observedAt, string sha, string message)
    {
        ArgumentException.ThrowIfNullOrEmpty(sha);
        ArgumentException.ThrowIfNullOrEmpty(message);

        string truncatedSha = sha.Length > ShaMaxLength ? sha[..ShaMaxLength] : sha;
        string truncatedMessage = message.Length > MessageMaxLength ? message[..MessageMaxLength] : message;

        return new CommitMarker(observedAt, truncatedSha, truncatedMessage);
    }

    /// <summary>
    /// Reconstructs a <see cref="CommitMarker"/> verbatim from trusted storage (e.g. database deserialization).
    /// No truncation or validation is applied.
    /// </summary>
    public static CommitMarker Parse(DateTimeOffset observedAt, string sha, string message)
    {
        return new CommitMarker(observedAt, sha, message);
    }
}
