using System.Text.Json;

using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Infrastructure.AuthModeJsonConverterTests;

public sealed class Serialize
{
    private readonly JsonSerializerOptions _options;

    public Serialize()
    {
        _options = new JsonSerializerOptions();
        _options.Converters.Add(new AuthModeJsonConverter());
    }

    [Fact]
    public void WhenApiKeyAuthModeSerialised_ContainsTypeDiscriminator()
    {
        // Arrange
        AuthMode authMode = new AuthMode.ApiKey("encrypted-key-value");

        // Act
        string json = JsonSerializer.Serialize(authMode, _options);

        // Assert
        json.ShouldContain(@"""type"":""api_key""");
    }

    [Fact]
    public void WhenApiKeyAuthModeSerialised_ContainsEncryptedKey()
    {
        // Arrange
        AuthMode authMode = new AuthMode.ApiKey("encrypted-key-value");

        // Act
        string json = JsonSerializer.Serialize(authMode, _options);

        // Assert
        json.ShouldContain(@"""encrypted_key"":""encrypted-key-value""");
    }

    [Fact]
    public void WhenOAuthAuthModeSerialised_ContainsTypeDiscriminator()
    {
        // Arrange
        DateTimeOffset expiresAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        AuthMode authMode = new AuthMode.OAuth("access-token", "refresh-token", expiresAt, "pro");

        // Act
        string json = JsonSerializer.Serialize(authMode, _options);

        // Assert
        json.ShouldContain(@"""type"":""oauth""");
    }

    [Fact]
    public void WhenOAuthAuthModeSerialised_ContainsAllFields()
    {
        // Arrange
        DateTimeOffset expiresAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        AuthMode authMode = new AuthMode.OAuth("access-token", "refresh-token", expiresAt, "pro");

        // Act
        string json = JsonSerializer.Serialize(authMode, _options);

        // Assert
        json.ShouldSatisfyAllConditions(
            () => json.ShouldContain(@"""access_token"":""access-token"""),
            () => json.ShouldContain(@"""refresh_token"":""refresh-token"""),
            () => json.ShouldContain(@"""subscription_type"":""pro"""));
    }
}
