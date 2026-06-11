using System.Text.Json;

using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Infrastructure.AuthModeJsonConverterTests;

public sealed class Deserialize
{
    private readonly JsonSerializerOptions _options;

    public Deserialize()
    {
        _options = new JsonSerializerOptions();
        _options.Converters.Add(new AuthModeJsonConverter());
    }

    [Fact]
    public void WhenApiKeyJsonDeserialised_ReturnsApiKeyAuthMode()
    {
        // Arrange
        string json = @"{""type"":""api_key"",""encrypted_key"":""my-key""}";

        // Act
        AuthMode? result = JsonSerializer.Deserialize<AuthMode>(json, _options);

        // Assert
        AuthMode.ApiKey apiKey = result.ShouldBeOfType<AuthMode.ApiKey>();
        apiKey.Key.ShouldBe("my-key");
    }

    [Fact]
    public void WhenOAuthJsonDeserialised_ReturnsOAuthAuthMode()
    {
        // Arrange
        DateTimeOffset expiresAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string json = @$"{{""type"":""oauth"",""access_token"":""at"",""refresh_token"":""rt"",""expires_at"":""{expiresAt:O}"",""subscription_type"":""pro""}}";

        // Act
        AuthMode? result = JsonSerializer.Deserialize<AuthMode>(json, _options);

        // Assert
        AuthMode.OAuth oauth = result.ShouldBeOfType<AuthMode.OAuth>();
        oauth.ShouldSatisfyAllConditions(
            () => oauth.AccessToken.ShouldBe("at"),
            () => oauth.RefreshToken.ShouldBe("rt"),
            () => oauth.ExpiresAt.ShouldBe(expiresAt),
            () => oauth.SubscriptionType.ShouldBe("pro"));
    }

    [Fact]
    public void WhenUnknownTypeDiscriminator_ThrowsJsonException()
    {
        // Arrange
        string json = @"{""type"":""unknown"",""value"":""x""}";

        // Act
        Action act = () => JsonSerializer.Deserialize<AuthMode>(json, _options);

        // Assert
        Should.Throw<JsonException>(act);
    }

    [Fact]
    public void WhenApiKeyAuthModeRoundTripped_PreservesAllData()
    {
        // Arrange
        AuthMode original = new AuthMode.ApiKey("round-trip-key");

        // Act
        string json = JsonSerializer.Serialize(original, _options);
        AuthMode? result = JsonSerializer.Deserialize<AuthMode>(json, _options);

        // Assert
        AuthMode.ApiKey apiKey = result.ShouldBeOfType<AuthMode.ApiKey>();
        apiKey.Key.ShouldBe("round-trip-key");
    }

    [Fact]
    public void WhenOAuthAuthModeRoundTripped_PreservesAllData()
    {
        // Arrange
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow;
        AuthMode original = new AuthMode.OAuth("access", "refresh", expiresAt, "free");

        // Act
        string json = JsonSerializer.Serialize(original, _options);
        AuthMode? result = JsonSerializer.Deserialize<AuthMode>(json, _options);

        // Assert
        AuthMode.OAuth oauth = result.ShouldBeOfType<AuthMode.OAuth>();
        oauth.ShouldSatisfyAllConditions(
            () => oauth.AccessToken.ShouldBe("access"),
            () => oauth.RefreshToken.ShouldBe("refresh"),
            () => oauth.ExpiresAt.ShouldBe(expiresAt),
            () => oauth.SubscriptionType.ShouldBe("free"));
    }
}
