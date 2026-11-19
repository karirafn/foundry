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
    public void WhenDeniedWithProbedAtSerialized_RoundTripsProbedAt()
    {
        // Arrange
        DateTimeOffset probedAt = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        WriteProbeVerdict verdict = new WriteProbeVerdict.Denied(probedAt);

        // Act
        string json = JsonSerializer.Serialize(verdict, Options);
        WriteProbeVerdict? deserialized = JsonSerializer.Deserialize<WriteProbeVerdict>(json, Options);

        // Assert
        deserialized.ShouldBeOfType<WriteProbeVerdict.Denied>().ProbedAt.ShouldBe(probedAt);
    }

    [Fact]
    public void WhenDeniedLegacyJsonWithoutProbedAtField_DeserializesWithNullProbedAt()
    {
        // Arrange — rows persisted before ProbedAt was added carry only the discriminator.
        const string legacyJson = """{"$type":"denied"}""";

        // Act
        WriteProbeVerdict? deserialized = JsonSerializer.Deserialize<WriteProbeVerdict>(legacyJson, Options);

        // Assert
        deserialized.ShouldBeOfType<WriteProbeVerdict.Denied>().ProbedAt.ShouldBeNull();
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

    [Fact]
    public void WhenUnknownWithRateLimitedReason_RoundTripsReason()
    {
        // Arrange
        WriteProbeVerdict verdict = new WriteProbeVerdict.Unknown(Reason: UnknownReason.RateLimited);

        // Act
        string json = JsonSerializer.Serialize(verdict, Options);
        WriteProbeVerdict? deserialized = JsonSerializer.Deserialize<WriteProbeVerdict>(json, Options);

        // Assert
        WriteProbeVerdict.Unknown unknown = deserialized.ShouldBeOfType<WriteProbeVerdict.Unknown>();
        unknown.Reason.ShouldBe(UnknownReason.RateLimited);
    }

    [Fact]
    public void WhenUnknownWithTransportReason_RoundTripsReason()
    {
        // Arrange
        WriteProbeVerdict verdict = new WriteProbeVerdict.Unknown(Reason: UnknownReason.Transport);

        // Act
        string json = JsonSerializer.Serialize(verdict, Options);
        WriteProbeVerdict? deserialized = JsonSerializer.Deserialize<WriteProbeVerdict>(json, Options);

        // Assert
        WriteProbeVerdict.Unknown unknown = deserialized.ShouldBeOfType<WriteProbeVerdict.Unknown>();
        unknown.Reason.ShouldBe(UnknownReason.Transport);
    }

    [Fact]
    public void WhenUnknownLegacyJsonWithoutReasonField_DeserializesToTransport()
    {
        // Arrange — legacy rows persisted before the Reason field was added omit the property;
        // they must round-trip to Transport (the default/safe value) for backward compatibility.
        const string legacyJson = """{"$type":"unknown","LastAttemptedAt":"2026-08-01T00:00:00+00:00"}""";

        // Act
        WriteProbeVerdict? deserialized = JsonSerializer.Deserialize<WriteProbeVerdict>(legacyJson, Options);

        // Assert
        WriteProbeVerdict.Unknown unknown = deserialized.ShouldBeOfType<WriteProbeVerdict.Unknown>();
        unknown.Reason.ShouldBe(UnknownReason.Transport);
    }

    [Fact]
    public void WhenUnknownDefaultConstruct_ReasonIsTransport()
    {
        // Arrange / Act
        WriteProbeVerdict.Unknown unknown = new WriteProbeVerdict.Unknown();

        // Assert
        unknown.Reason.ShouldBe(UnknownReason.Transport);
    }

    [Fact]
    public void WhenDeniedWithMissingPermissionsSerialized_RoundTripsMissingPermissions()
    {
        // Arrange
        WriteProbeVerdict verdict = new WriteProbeVerdict.Denied()
        {
            MissingPermissions = ["administration", "repository_hooks"],
        };

        // Act
        string json = JsonSerializer.Serialize(verdict, Options);
        WriteProbeVerdict? deserialized = JsonSerializer.Deserialize<WriteProbeVerdict>(json, Options);

        // Assert
        deserialized.ShouldBeOfType<WriteProbeVerdict.Denied>()
            .MissingPermissions.ShouldBe(["administration", "repository_hooks"]);
    }

    [Fact]
    public void WhenDeniedLegacyJsonWithoutMissingPermissions_DeserializesWithEmptySet()
    {
        // Arrange
        const string legacyJson = """{"$type":"denied","ProbedAt":"2026-11-19T10:00:00+00:00"}""";

        // Act
        WriteProbeVerdict? deserialized = JsonSerializer.Deserialize<WriteProbeVerdict>(legacyJson, Options);

        // Assert
        deserialized.ShouldBeOfType<WriteProbeVerdict.Denied>().MissingPermissions.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("granted")]
    [InlineData("denied")]
    [InlineData("unknown")]
    public void WhenVerdictSerializedWithPermissionListVersion_RoundTripsTheVersion(string kind)
    {
        // Arrange
        WriteProbeVerdict verdict = kind switch
        {
            "granted" => new WriteProbeVerdict.Granted { PermissionListVersion = "github-0123456789abcdef" },
            "denied" => new WriteProbeVerdict.Denied { PermissionListVersion = "github-0123456789abcdef" },
            _ => new WriteProbeVerdict.Unknown { PermissionListVersion = "github-0123456789abcdef" },
        };

        // Act
        string json = JsonSerializer.Serialize(verdict, Options);
        WriteProbeVerdict? deserialized = JsonSerializer.Deserialize<WriteProbeVerdict>(json, Options);

        // Assert
        deserialized.ShouldNotBeNull().PermissionListVersion.ShouldBe("github-0123456789abcdef");
    }

    [Fact]
    public void WhenGrantedLegacyJsonWithoutVersion_DeserializesWithNullVersion()
    {
        // Arrange
        const string legacyJson = """{"$type":"granted"}""";

        // Act
        WriteProbeVerdict? deserialized = JsonSerializer.Deserialize<WriteProbeVerdict>(legacyJson, Options);

        // Assert
        deserialized.ShouldBeOfType<WriteProbeVerdict.Granted>().PermissionListVersion.ShouldBeNull();
    }

    [Fact]
    public void DeniedVerdictsWithTheSameMissingPermissions_AreEqual()
    {
        // Arrange
        WriteProbeVerdict.Denied first = new() { MissingPermissions = ["contents"] };
        WriteProbeVerdict.Denied second = new() { MissingPermissions = ["contents"] };

        // Act
        bool equal = first.Equals(second);

        // Assert
        equal.ShouldBeTrue();
    }
}
