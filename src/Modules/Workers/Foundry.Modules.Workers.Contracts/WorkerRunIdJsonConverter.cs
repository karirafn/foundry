using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundry.Modules.Workers.Contracts;

public sealed class WorkerRunIdJsonConverter : JsonConverter<WorkerRunId>
{
    public override WorkerRunId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        string? value = reader.GetString();

        if (value is null || !Guid.TryParse(value, out Guid guid))
        {
            throw new JsonException("Expected a GUID string for WorkerRunId.");
        }

        return WorkerRunId.From(guid);
    }

    public override void Write(
        Utf8JsonWriter writer,
        WorkerRunId value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value.ToString("D"));
    }
}
