using System.Text.Json;

namespace Foundry.Shared.Infrastructure.Outbox;

public static class OutboxSerializerOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = false,
    };
}
