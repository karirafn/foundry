using System.Text.Json;

using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Infrastructure.SpendStateJsonConverterTests;

public sealed class RoundTrip
{
    private readonly JsonSerializerOptions _options;

    public RoundTrip()
    {
        _options = new JsonSerializerOptions();
        _options.Converters.Add(new SpendStateJsonConverter());
    }

    [Fact]
    public void WhenAvailableRoundTripped_PreservesType()
    {
        // Arrange
        SpendState original = new SpendState.Available();

        // Act
        string json = JsonSerializer.Serialize(original, _options);
        SpendState? result = JsonSerializer.Deserialize<SpendState>(json, _options);

        // Assert
        result.ShouldBeOfType<SpendState.Available>();
    }

    [Fact]
    public void WhenBlockedRoundTripped_PreservesType()
    {
        // Arrange
        DateTimeOffset nextProbeAt = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
        SpendState original = new SpendState.Blocked(nextProbeAt);

        // Act
        string json = JsonSerializer.Serialize(original, _options);
        SpendState? result = JsonSerializer.Deserialize<SpendState>(json, _options);

        // Assert
        result.ShouldBeOfType<SpendState.Blocked>();
    }

    [Fact]
    public void WhenBlockedRoundTripped_PreservesNextProbeAt()
    {
        // Arrange
        DateTimeOffset nextProbeAt = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
        SpendState original = new SpendState.Blocked(nextProbeAt);

        // Act
        string json = JsonSerializer.Serialize(original, _options);
        SpendState.Blocked? result = JsonSerializer.Deserialize<SpendState>(json, _options) as SpendState.Blocked;

        // Assert
        result.ShouldNotBeNull();
        result.NextProbeAt.ShouldBe(nextProbeAt);
    }

    [Fact]
    public void WhenBlockedSerialized_IncludesNextProbeAtField()
    {
        // Arrange
        DateTimeOffset nextProbeAt = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
        SpendState original = new SpendState.Blocked(nextProbeAt);

        // Act
        string json = JsonSerializer.Serialize(original, _options);

        // Assert
        json.ShouldContain("next_probe_at");
    }

    [Fact]
    public void WhenUnknownTypeDiscriminator_ThrowsJsonException()
    {
        // Arrange
        string json = @"{""type"":""unknown""}";

        // Act
        Action act = () => JsonSerializer.Deserialize<SpendState>(json, _options);

        // Assert
        Should.Throw<JsonException>(act);
    }
}
