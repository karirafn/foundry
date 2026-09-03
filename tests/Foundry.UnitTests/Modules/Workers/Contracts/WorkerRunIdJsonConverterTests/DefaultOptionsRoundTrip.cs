using System.Text.Json;
using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.WorkerRunIdJsonConverterTests;

/// <summary>
/// Guard: proves that the type-level [JsonConverter] attribute on WorkerRunId makes it
/// round-trip as a flat UUID string with default JsonSerializerOptions — the same path
/// used by ReadFromJsonAsync in the integration tests and any plain consumer.
/// </summary>
public sealed class DefaultOptionsRoundTrip
{
    private static readonly JsonSerializerOptions DefaultWebOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void WhenSerializedWithDefaultOptions_EmitsBareGuidString()
    {
        // Arrange
        WorkerRunId id = WorkerRunId.New();

        // Act
        string json = JsonSerializer.Serialize(id, DefaultWebOptions);

        // Assert
        json.ShouldBe($"\"{id.Value:D}\"");
    }

    [Fact]
    public void WhenWorkerRunDetailSerializedWithDefaultOptions_WorkerRunIdRoundTripsFlat()
    {
        // Arrange
        WorkerRunId id = WorkerRunId.New();
        WorkerRunDetail detail = new(
            id,
            Guid.NewGuid(),
            "failed",
            "non_zero_exit",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false);

        // Act
        string json = JsonSerializer.Serialize(detail, DefaultWebOptions);
        WorkerRunDetail? deserialized = JsonSerializer.Deserialize<WorkerRunDetail>(json, DefaultWebOptions);

        // Assert
        JsonNode? node = JsonNode.Parse(json);
        string? workerRunIdNode = node?["workerRunId"]?.GetValue<string>();
        workerRunIdNode.ShouldNotBeNull();
        workerRunIdNode.ShouldBe(id.Value.ToString("D"));

        deserialized.ShouldNotBeNull();
        deserialized.WorkerRunId.ShouldBe(id);
    }
}
