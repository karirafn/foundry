using Foundry.Modules.Settings.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsTests;

public sealed class SetOAuthAccountIdentity
{
    [Fact]
    public void WhenCalled_SetsOAuthAccountEmail()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.SetOAuthAccountIdentity("user@example.com", "MyOrg", "pro");

        // Assert
        settings.OAuthAccountEmail.ShouldBe("user@example.com");
    }

    [Fact]
    public void WhenCalled_SetsOAuthAccountOrgName()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.SetOAuthAccountIdentity("user@example.com", "MyOrg", "pro");

        // Assert
        settings.OAuthAccountOrgName.ShouldBe("MyOrg");
    }

    [Fact]
    public void WhenCalled_UpdatesAuthModeSubscriptionType()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.SetAuthMode(new AuthMode.OAuth("old-plan"));

        // Act
        settings.SetOAuthAccountIdentity("user@example.com", "MyOrg", "pro");

        // Assert
        AuthMode.OAuth oauth = settings.AuthMode.ShouldBeOfType<AuthMode.OAuth>();
        oauth.SubscriptionType.ShouldBe("pro");
    }

    [Fact]
    public void WhenCalledWithNullFields_SetsNullsPreservingAuthModeType()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.SetAuthMode(new AuthMode.OAuth("pro"));

        // Act
        settings.SetOAuthAccountIdentity(null, null, null);

        // Assert
        settings.ShouldSatisfyAllConditions(
            () => settings.OAuthAccountEmail.ShouldBeNull(),
            () => settings.OAuthAccountOrgName.ShouldBeNull(),
            () => settings.AuthMode.ShouldBeOfType<AuthMode.OAuth>());
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.SetOAuthAccountIdentity("user@example.com", "MyOrg", "pro");

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenCalled_DoesNotChangeCreatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset createdAt = settings.CreatedAt;

        // Act
        settings.SetOAuthAccountIdentity("user@example.com", "MyOrg", "pro");

        // Assert
        settings.CreatedAt.ShouldBe(createdAt);
    }
}
