using Foundry.Modules.Settings.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.AuthModeTests;

public sealed class Subtypes
{
    [Fact]
    public void WhenApiKeyCreated_StoresKey()
    {
        // Arrange
        const string key = "plaintext-api-key";

        // Act
        AuthMode mode = new AuthMode.ApiKey(key);

        // Assert
        AuthMode.ApiKey apiKey = mode.ShouldBeOfType<AuthMode.ApiKey>();
        apiKey.Key.ShouldBe(key);
    }

    [Fact]
    public void WhenOAuthCreated_StoresAllFields()
    {
        // Arrange
        const string accessToken = "access-token";
        const string refreshToken = "refresh-token";
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        const string subscriptionType = "pro";

        // Act
        AuthMode mode = new AuthMode.OAuth(accessToken, refreshToken, expiresAt, subscriptionType);

        // Assert
        AuthMode.OAuth oauth = mode.ShouldBeOfType<AuthMode.OAuth>();
        oauth.ShouldSatisfyAllConditions(
            () => oauth.AccessToken.ShouldBe(accessToken),
            () => oauth.RefreshToken.ShouldBe(refreshToken),
            () => oauth.ExpiresAt.ShouldBe(expiresAt),
            () => oauth.SubscriptionType.ShouldBe(subscriptionType));
    }

    [Fact]
    public void ApiKey_IsAuthMode()
    {
        // Arrange & Act
        AuthMode mode = new AuthMode.ApiKey("key");

        // Assert
        mode.ShouldBeAssignableTo<AuthMode>();
    }

    [Fact]
    public void OAuth_IsAuthMode()
    {
        // Arrange & Act
        AuthMode mode = new AuthMode.OAuth("access", "refresh", DateTimeOffset.UtcNow, "pro");

        // Assert
        mode.ShouldBeAssignableTo<AuthMode>();
    }
}
