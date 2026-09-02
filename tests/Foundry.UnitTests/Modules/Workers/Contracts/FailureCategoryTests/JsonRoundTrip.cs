using System.Text.Json;

using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.FailureCategoryTests;

public sealed class JsonRoundTrip
{
    [Theory]
    [InlineData(FailureCategory.NonZeroExitToken)]
    [InlineData(FailureCategory.TimedOutToken)]
    [InlineData(FailureCategory.ContainerErrorToken)]
    [InlineData(FailureCategory.UsageLimitedToken)]
    [InlineData(FailureCategory.WorkerBootstrapFailedToken)]
    [InlineData(FailureCategory.AuthInvalidToken)]
    [InlineData(FailureCategory.ProviderErrorToken)]
    [InlineData(FailureCategory.TransientApiErrorToken)]
    [InlineData(FailureCategory.CreditsExhaustedToken)]
    [InlineData(FailureCategory.PrClosedToken)]
    public void WhenSerialized_EmitsFlatTokenString(string token)
    {
        // Arrange
        FailureCategory category = FailureCategory.FromToken(token);

        // Act
        string json = JsonSerializer.Serialize(category);

        // Assert
        json.ShouldBe($"\"{token}\"");
    }

    [Theory]
    [InlineData(FailureCategory.NonZeroExitToken)]
    [InlineData(FailureCategory.TimedOutToken)]
    [InlineData(FailureCategory.ContainerErrorToken)]
    [InlineData(FailureCategory.UsageLimitedToken)]
    [InlineData(FailureCategory.WorkerBootstrapFailedToken)]
    [InlineData(FailureCategory.AuthInvalidToken)]
    [InlineData(FailureCategory.ProviderErrorToken)]
    [InlineData(FailureCategory.TransientApiErrorToken)]
    [InlineData(FailureCategory.CreditsExhaustedToken)]
    [InlineData(FailureCategory.PrClosedToken)]
    public void WhenDeserializedFromTokenString_ProducesEqualCategory(string token)
    {
        // Arrange
        string json = $"\"{token}\"";

        // Act
        FailureCategory? deserialized = JsonSerializer.Deserialize<FailureCategory>(json);

        // Assert
        FailureCategory expected = FailureCategory.FromToken(token);
        deserialized.ShouldBe(expected);
    }

    [Fact]
    public void WhenDeserializedFromNumber_ThrowsJsonException()
    {
        // Arrange
        const string json = "42";

        // Act
        Action act = () => JsonSerializer.Deserialize<FailureCategory>(json);

        // Assert
        JsonException ex = Should.Throw<JsonException>(act);
        ex.Message.ShouldContain(nameof(FailureCategory));
    }

    [Fact]
    public void WhenDeserializedFromUnknownToken_ThrowsJsonException()
    {
        // Arrange
        const string json = "\"bogus_token\"";

        // Act
        Action act = () => JsonSerializer.Deserialize<FailureCategory>(json);

        // Assert
        Should.Throw<JsonException>(act);
    }
}
