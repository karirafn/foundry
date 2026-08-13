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
        SpendState original = new SpendState.Blocked();

        // Act
        string json = JsonSerializer.Serialize(original, _options);
        SpendState? result = JsonSerializer.Deserialize<SpendState>(json, _options);

        // Assert
        result.ShouldBeOfType<SpendState.Blocked>();
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
