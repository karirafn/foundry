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

    public static CommitMarker Create(DateTimeOffset observedAt, string sha, string message)
    {
        return new CommitMarker(observedAt, sha, message);
    }

    public static CommitMarker Parse(DateTimeOffset observedAt, string sha, string message)
    {
        return new CommitMarker(observedAt, sha, message);
    }
}
