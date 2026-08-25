using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.WriteProbeVerdictTests;

public sealed class Serialization
{
    private static readonly JsonSerializerOptions Options = new();

    [Fact]
    public void WhenGrantedSerialized_RoundTripsToGranted()
    {
        // Arrange
        WriteProbeVerdict verdict = new WriteProbeVerdict.Granted();

        // Act
        string json = JsonSerializer.Serialize(verdict, Options);
        WriteProbeVerdict? deserialized = JsonSerializer.Deserialize<WriteProbeVerdict>(json, Options);

        // Assert
        deserialized.ShouldBeOfType<WriteProbeVerdict.Granted>();
    }

    [Fact]
    public void WhenDeniedSerialized_RoundTripsToDenied()
    {
        // Arrange
        WriteProbeVerdict verdict = new WriteProbeVerdict.Denied();

        // Act
        string json = JsonSerializer.Serialize(verdict, Options);
        WriteProbeVerdict? deserialized = JsonSerializer.Deserialize<WriteProbeVerdict>(json, Options);

        // Assert
        deserialized.ShouldBeOfType<WriteProbeVerdict.Denied>();
    }

    [Fact]
    public void WhenUnknownSerialized_RoundTripsToUnknown()
    {
        // Arrange
        WriteProbeVerdict verdict = new WriteProbeVerdict.Unknown();

        // Act
        string json = JsonSerializer.Serialize(verdict, Options);
        WriteProbeVerdict? deserialized = JsonSerializer.Deserialize<WriteProbeVerdict>(json, Options);

        // Assert
        deserialized.ShouldBeOfType<WriteProbeVerdict.Unknown>();
    }

    [Fact]
    public void WhenGrantedDiscriminator_SerializesWithGrantedType()
    {
        // Arrange
        WriteProbeVerdict verdict = new WriteProbeVerdict.Granted();

        // Act
        string json = JsonSerializer.Serialize(verdict, Options);

        // Assert
        json.ShouldContain("granted");
    }

    [Fact]
    public void WhenDeniedDiscriminator_SerializesWithDeniedType()
    {
        // Arrange
        WriteProbeVerdict verdict = new WriteProbeVerdict.Denied();

        // Act
        string json = JsonSerializer.Serialize(verdict, Options);

        // Assert
        json.ShouldContain("denied");
    }

    [Fact]
    public void WhenUnknownDiscriminator_SerializesWithUnknownType()
    {
        // Arrange
        WriteProbeVerdict verdict = new WriteProbeVerdict.Unknown();

        // Act
        string json = JsonSerializer.Serialize(verdict, Options);

        // Assert
        json.ShouldContain("unknown");
    }

    [Fact]
    public void WhenUnknownWithTimestampSerialized_RoundTripsTimestamp()
    {
        // Arrange
        DateTimeOffset attempt = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);
        WriteProbeVerdict verdict = new WriteProbeVerdict.Unknown(attempt);

        // Act
        string json = JsonSerializer.Serialize(verdict, Options);
        WriteProbeVerdict? deserialized = JsonSerializer.Deserialize<WriteProbeVerdict>(json, Options);

        // Assert
        WriteProbeVerdict.Unknown unknown = deserialized.ShouldBeOfType<WriteProbeVerdict.Unknown>();
        unknown.LastAttemptedAt.ShouldBe(attempt);
    }

    [Fact]
    public void WhenUnknownWithoutTimestampSerialized_RoundTripsNullTimestamp()
    {
        // Arrange
        WriteProbeVerdict verdict = new WriteProbeVerdict.Unknown();

        // Act
        string json = JsonSerializer.Serialize(verdict, Options);
        WriteProbeVerdict? deserialized = JsonSerializer.Deserialize<WriteProbeVerdict>(json, Options);

        // Assert
        WriteProbeVerdict.Unknown unknown = deserialized.ShouldBeOfType<WriteProbeVerdict.Unknown>();
        unknown.LastAttemptedAt.ShouldBeNull();
    }

    [Fact]
    public void WhenMalformedJson_DeserializeThrowsJsonException()
    {
        // Arrange — documents WHY MonitoredRepositoryConfiguration wraps deserialization in try/catch
        const string malformedJson = "{ \"$type\": \"unrecognized_discriminator\" }";

        // Act
        JsonException ex = Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<WriteProbeVerdict>(malformedJson, Options));

        // Assert
        ex.ShouldNotBeNull();
    }
}
