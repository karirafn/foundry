using System.Text.Json;

using Foundry.Shared;

namespace Foundry.Shared.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? Error { get; private set; }

    public static OutboxMessage Create(IIntegrationEvent @event, DateTimeOffset now)
    {
        System.Type eventType = @event.GetType();

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = eventType.AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(@event, eventType, OutboxSerializerOptions.Default),
            OccurredAt = now,
            Attempts = 0,
            ProcessedAt = null,
            Error = null,
        };
    }

    public void MarkPublished(DateTimeOffset now)
    {
        ProcessedAt = now;
    }

    public void RecordFailure(string error)
    {
        Attempts++;
        Error = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
    }

    private const int MaxErrorLength = 2000;
}
