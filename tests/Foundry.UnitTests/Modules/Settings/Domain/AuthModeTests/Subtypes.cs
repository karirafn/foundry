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
    public void WhenOAuthCreated_StoresSubscriptionType()
    {
        // Arrange
        const string subscriptionType = "pro";

        // Act
        AuthMode mode = new AuthMode.OAuth(subscriptionType);

        // Assert
        AuthMode.OAuth oauth = mode.ShouldBeOfType<AuthMode.OAuth>();
        oauth.SubscriptionType.ShouldBe(subscriptionType);
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
        AuthMode mode = new AuthMode.OAuth("pro");

        // Assert
        mode.ShouldBeAssignableTo<AuthMode>();
    }
}
