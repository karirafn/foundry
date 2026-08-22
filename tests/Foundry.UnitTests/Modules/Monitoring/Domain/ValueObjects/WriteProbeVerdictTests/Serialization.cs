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
}
