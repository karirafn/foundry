using System.Text.Json;

using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.WorkerRunIdJsonConverterTests;

public sealed class RoundTrip
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new WorkerRunIdJsonConverter() },
    };

    [Fact]
    public void WhenSerialized_EmitsBareGuidString()
    {
        // Arrange
        WorkerRunId id = WorkerRunId.New();

        // Act
        string json = JsonSerializer.Serialize(id, Options);

        // Assert
        json.ShouldBe($"\"{id.Value:D}\"");
    }

    [Fact]
    public void WhenDeserializedFromGuidString_ProducesEqualWorkerRunId()
    {
        // Arrange
        WorkerRunId original = WorkerRunId.New();
        string json = $"\"{original.Value:D}\"";

        // Act
        WorkerRunId deserialized = JsonSerializer.Deserialize<WorkerRunId>(json, Options);

        // Assert
        deserialized.ShouldBe(original);
    }
}
