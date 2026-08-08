using System.Text.Json;

using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Infrastructure.ImageBuildStateJsonConverterTests;

public sealed class RoundTrip
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new ImageBuildStateJsonConverter() },
    };

    [Fact]
    public void WhenFailedStateWithRetryFields_RoundTripsNextRetryAt()
    {
        // Arrange
        DateTimeOffset nextRetryAt = new DateTimeOffset(2026, 8, 8, 12, 30, 0, TimeSpan.Zero);
        ImageBuildState.Failed original = new(
            ErrorTail: "build error",
            NextRetryAt: nextRetryAt,
            Attempt: 3);

        // Act
        string json = JsonSerializer.Serialize<ImageBuildState>(original, Options);
        ImageBuildState result = JsonSerializer.Deserialize<ImageBuildState>(json, Options)!;

        // Assert
        ImageBuildState.Failed failed = result.ShouldBeOfType<ImageBuildState.Failed>();
        failed.NextRetryAt.ShouldBe(nextRetryAt);
    }

    [Fact]
    public void WhenFailedStateWithRetryFields_RoundTripsAttempt()
    {
        // Arrange
        ImageBuildState.Failed original = new(
            ErrorTail: "build error",
            NextRetryAt: DateTimeOffset.UtcNow.AddSeconds(30),
            Attempt: 3);

        // Act
        string json = JsonSerializer.Serialize<ImageBuildState>(original, Options);
        ImageBuildState result = JsonSerializer.Deserialize<ImageBuildState>(json, Options)!;

        // Assert
        ImageBuildState.Failed failed = result.ShouldBeOfType<ImageBuildState.Failed>();
        failed.Attempt.ShouldBe(3);
    }

    [Fact]
    public void WhenOldFailedJsonWithNoRetryFields_NextRetryAtIsNull()
    {
        // Arrange — old persisted row has no next_retry_at or attempt fields
        const string oldJson = """{"type":"failed","error_tail":"old error"}""";

        // Act
        ImageBuildState result = JsonSerializer.Deserialize<ImageBuildState>(oldJson, Options)!;

        // Assert
        ImageBuildState.Failed failed = result.ShouldBeOfType<ImageBuildState.Failed>();
        failed.NextRetryAt.ShouldBeNull();
    }

    [Fact]
    public void WhenOldFailedJsonWithNoRetryFields_AttemptIsZero()
    {
        // Arrange — old persisted row has no next_retry_at or attempt fields
        const string oldJson = """{"type":"failed","error_tail":"old error"}""";

        // Act
        ImageBuildState result = JsonSerializer.Deserialize<ImageBuildState>(oldJson, Options)!;

        // Assert
        ImageBuildState.Failed failed = result.ShouldBeOfType<ImageBuildState.Failed>();
        failed.Attempt.ShouldBe(0);
    }

    [Fact]
    public void WhenFailedStateWithNullNextRetryAt_RoundTripsAsNull()
    {
        // Arrange
        ImageBuildState.Failed original = new(
            ErrorTail: "build error",
            NextRetryAt: null,
            Attempt: 0);

        // Act
        string json = JsonSerializer.Serialize<ImageBuildState>(original, Options);
        ImageBuildState result = JsonSerializer.Deserialize<ImageBuildState>(json, Options)!;

        // Assert
        ImageBuildState.Failed failed = result.ShouldBeOfType<ImageBuildState.Failed>();
        failed.NextRetryAt.ShouldBeNull();
    }

    [Fact]
    public void WhenFailedState_JsonContainsSnakeCaseNextRetryAt()
    {
        // Arrange
        DateTimeOffset nextRetryAt = new DateTimeOffset(2026, 8, 8, 12, 30, 0, TimeSpan.Zero);
        ImageBuildState.Failed original = new(
            ErrorTail: "error",
            NextRetryAt: nextRetryAt,
            Attempt: 1);

        // Act
        string json = JsonSerializer.Serialize<ImageBuildState>(original, Options);

        // Assert
        json.ShouldContain("next_retry_at");
    }

    [Fact]
    public void WhenFailedState_JsonContainsAttemptField()
    {
        // Arrange
        ImageBuildState.Failed original = new(
            ErrorTail: "error",
            NextRetryAt: null,
            Attempt: 2);

        // Act
        string json = JsonSerializer.Serialize<ImageBuildState>(original, Options);

        // Assert
        json.ShouldContain("\"attempt\"");
    }

    [Fact]
    public void WhenIdleState_RoundTrips()
    {
        // Arrange
        ImageBuildState original = new ImageBuildState.Idle();

        // Act
        string json = JsonSerializer.Serialize<ImageBuildState>(original, Options);
        ImageBuildState result = JsonSerializer.Deserialize<ImageBuildState>(json, Options)!;

        // Assert
        result.ShouldBeOfType<ImageBuildState.Idle>();
    }

    [Fact]
    public void WhenBuildingState_RoundTrips()
    {
        // Arrange
        ImageBuildState original = new ImageBuildState.Building();

        // Act
        string json = JsonSerializer.Serialize<ImageBuildState>(original, Options);
        ImageBuildState result = JsonSerializer.Deserialize<ImageBuildState>(json, Options)!;

        // Assert
        result.ShouldBeOfType<ImageBuildState.Building>();
    }

    [Fact]
    public void WhenFailedStateWithOversizedAttempt_ClampsToMaxAttempt()
    {
        // Arrange — a tampered or corrupted persisted row with an unreasonably large attempt value
        const string tamperedJson = """{"type":"failed","error_tail":"err","attempt":99999}""";

        // Act
        ImageBuildState result = JsonSerializer.Deserialize<ImageBuildState>(tamperedJson, Options)!;

        // Assert — attempt is clamped to a sane maximum; the exact max is an implementation detail
        // but must be much less than 99999 to prevent overflow in backoff computation
        ImageBuildState.Failed failed = result.ShouldBeOfType<ImageBuildState.Failed>();
        failed.Attempt.ShouldBeLessThan(1000);
    }

    [Fact]
    public void WhenFailedStateWithNegativeAttempt_ClampsToZero()
    {
        // Arrange — negative attempt is not meaningful; clamp to 0
        const string tamperedJson = """{"type":"failed","error_tail":"err","attempt":-5}""";

        // Act
        ImageBuildState result = JsonSerializer.Deserialize<ImageBuildState>(tamperedJson, Options)!;

        // Assert
        ImageBuildState.Failed failed = result.ShouldBeOfType<ImageBuildState.Failed>();
        failed.Attempt.ShouldBe(0);
    }
}
