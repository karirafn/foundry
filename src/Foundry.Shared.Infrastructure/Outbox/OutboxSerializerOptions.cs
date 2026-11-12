using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Foundry.Shared.Infrastructure.Outbox;

public static class OutboxSerializerOptions
{
    public static readonly JsonSerializerOptions Default = Build();

    private static JsonSerializerOptions Build()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.MakeReadOnly();
        return options;
    }
}
