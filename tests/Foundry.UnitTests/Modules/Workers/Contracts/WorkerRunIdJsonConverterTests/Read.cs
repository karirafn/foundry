using System.Text.Json;

using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.WorkerRunIdJsonConverterTests;

public sealed class Read
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new WorkerRunIdJsonConverter() },
    };

    [Fact]
    public void WhenTokenIsNumber_ThrowsJsonException()
    {
        // Arrange
        const string json = "42";

        // Act
        Action act = () => JsonSerializer.Deserialize<WorkerRunId>(json, Options);

        // Assert
        JsonException ex = Should.Throw<JsonException>(act);
        ex.Message.ShouldContain("WorkerRunId");
    }

    [Fact]
    public void WhenTokenIsNonGuidString_ThrowsJsonException()
    {
        // Arrange
        const string json = "\"not-a-guid\"";

        // Act
        Action act = () => JsonSerializer.Deserialize<WorkerRunId>(json, Options);

        // Assert
        Should.Throw<JsonException>(act);
    }

    [Fact]
    public void WhenTokenIsNull_ThrowsJsonException()
    {
        // Arrange
        const string json = "null";

        // Act
        Action act = () => JsonSerializer.Deserialize<WorkerRunId>(json, Options);

        // Assert
        Should.Throw<JsonException>(act);
    }
}
