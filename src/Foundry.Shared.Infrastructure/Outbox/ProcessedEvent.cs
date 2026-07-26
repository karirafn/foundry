namespace Foundry.Shared.Infrastructure.Outbox;

public sealed class ProcessedEvent
{
    private ProcessedEvent()
    {
    }

    public Guid EventId { get; private set; }

    public string Handler { get; private set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; private set; }

    public static ProcessedEvent For(Guid eventId, string handlerName, DateTimeOffset now)
    {
        return new ProcessedEvent
        {
            EventId = eventId,
            Handler = handlerName,
            ProcessedAt = now,
        };
    }
}
