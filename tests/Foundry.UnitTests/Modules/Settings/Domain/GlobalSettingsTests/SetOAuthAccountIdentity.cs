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

    [Fact]
    public void WhenEmailExceedsMaxLength_EmailIsTruncatedToMax()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        string oversizedEmail = new string('a', GlobalSettings.MaxOAuthAccountEmailLength + 10);

        // Act
        settings.SetOAuthAccountIdentity(oversizedEmail, "MyOrg", "pro");

        // Assert — email clamped to the domain invariant cap, not stored oversized
        settings.OAuthAccountEmail.ShouldNotBeNull()
            .Length.ShouldBe(GlobalSettings.MaxOAuthAccountEmailLength);
    }

    [Fact]
    public void WhenOrgNameExceedsMaxLength_OrgNameIsTruncatedToMax()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        string oversizedOrgName = new string('b', GlobalSettings.MaxOAuthAccountOrgNameLength + 10);

        // Act
        settings.SetOAuthAccountIdentity("user@example.com", oversizedOrgName, "pro");

        // Assert — org name clamped to the domain invariant cap, not stored oversized
        settings.OAuthAccountOrgName.ShouldNotBeNull()
            .Length.ShouldBe(GlobalSettings.MaxOAuthAccountOrgNameLength);
    }

    [Fact]
    public void WhenEmailAtExactMaxLength_EmailStoredUnchanged()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        string exactEmail = new string('a', GlobalSettings.MaxOAuthAccountEmailLength);

        // Act
        settings.SetOAuthAccountIdentity(exactEmail, "MyOrg", "pro");

        // Assert
        settings.OAuthAccountEmail.ShouldBe(exactEmail);
    }
}
