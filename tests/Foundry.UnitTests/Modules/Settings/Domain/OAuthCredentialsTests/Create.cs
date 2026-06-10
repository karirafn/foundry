using Foundry.Modules.Settings.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.OAuthCredentialsTests;

public sealed class Create
{
    [Fact]
    public void WhenAllFieldsProvided_StoresAllValues()
    {
        // Arrange
        const string accessToken = "access-token";
        const string refreshToken = "refresh-token";
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        const string subscriptionType = "pro";

        // Act
        OAuthCredentials credentials = new(accessToken, refreshToken, expiresAt, subscriptionType);

        // Assert
        credentials.ShouldSatisfyAllConditions(
            () => credentials.AccessToken.ShouldBe(accessToken),
            () => credentials.RefreshToken.ShouldBe(refreshToken),
            () => credentials.ExpiresAt.ShouldBe(expiresAt),
            () => credentials.SubscriptionType.ShouldBe(subscriptionType));
    }

    [Fact]
    public void WhenTwoCredentialsHaveSameValues_AreEqual()
    {
        // Arrange
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        // Act
        OAuthCredentials a = new("token", "refresh", expiresAt, "pro");
        OAuthCredentials b = new("token", "refresh", expiresAt, "pro");

        // Assert
        a.ShouldBe(b);
    }
}
