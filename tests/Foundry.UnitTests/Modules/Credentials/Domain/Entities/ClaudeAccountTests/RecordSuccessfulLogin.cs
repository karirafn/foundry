using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Domain.Entities.ClaudeAccountTests;

public sealed class RecordSuccessfulLogin
{
    [Fact]
    public void WhenCalled_SetsAuthModeToOAuth()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();

        // Act
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");

        // Assert
        AuthMode.OAuth oauth = account.AuthMode.ShouldBeOfType<AuthMode.OAuth>();
        oauth.SubscriptionType.ShouldBe("pro");
    }

    [Fact]
    public void WhenCalled_SetsOAuthEmail()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();

        // Act
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");

        // Assert
        account.OAuthAccountEmail.ShouldBe("user@example.com");
    }

    [Fact]
    public void WhenCalled_SetsOAuthOrgName()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();

        // Act
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");

        // Assert
        account.OAuthAccountOrgName.ShouldBe("MyOrg");
    }

    [Fact]
    public void WhenCalled_SetsValidityToValid()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.SetAuthMode(new AuthMode.ApiKey("key"));
        account.Invalidate("some_reason");

        // Act
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");

        // Assert
        account.Validity.ShouldBeOfType<CredentialValidity.Valid>();
    }

    [Fact]
    public void WhenCalledWithNullFields_SetsNullsAndOAuthMode()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();

        // Act
        account.RecordSuccessfulLogin(null, null, null);

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.OAuthAccountEmail.ShouldBeNull(),
            () => account.OAuthAccountOrgName.ShouldBeNull(),
            () => account.AuthMode.ShouldBeOfType<AuthMode.OAuth>());
    }

    [Fact]
    public void WhenEmailExceedsMaxLength_EmailIsTruncatedToMax()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        string oversizedEmail = new('a', ClaudeAccount.MaxOAuthAccountEmailLength + 10);

        // Act
        account.RecordSuccessfulLogin(oversizedEmail, "MyOrg", "pro");

        // Assert
        account.OAuthAccountEmail.ShouldNotBeNull()
            .Length.ShouldBe(ClaudeAccount.MaxOAuthAccountEmailLength);
    }

    [Fact]
    public void WhenOrgNameExceedsMaxLength_OrgNameIsTruncatedToMax()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        string oversizedOrgName = new('b', ClaudeAccount.MaxOAuthAccountOrgNameLength + 10);

        // Act
        account.RecordSuccessfulLogin("user@example.com", oversizedOrgName, "pro");

        // Assert
        account.OAuthAccountOrgName.ShouldNotBeNull()
            .Length.ShouldBe(ClaudeAccount.MaxOAuthAccountOrgNameLength);
    }

    [Fact]
    public void WhenEmailAtExactMaxLength_EmailStoredUnchanged()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        string exactEmail = new('a', ClaudeAccount.MaxOAuthAccountEmailLength);

        // Act
        account.RecordSuccessfulLogin(exactEmail, "MyOrg", "pro");

        // Assert
        account.OAuthAccountEmail.ShouldBe(exactEmail);
    }

    [Fact]
    public void WhenOrgNameAtExactMaxLength_OrgNameStoredUnchanged()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        string exactOrgName = new('b', ClaudeAccount.MaxOAuthAccountOrgNameLength);

        // Act
        account.RecordSuccessfulLogin("user@example.com", exactOrgName, "pro");

        // Assert
        account.OAuthAccountOrgName.ShouldBe(exactOrgName);
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        DateTimeOffset before = account.UpdatedAt;

        // Act
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");

        // Assert
        account.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenCalled_DoesNotChangeCreatedAt()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        DateTimeOffset createdAt = account.CreatedAt;

        // Act
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");

        // Assert
        account.CreatedAt.ShouldBe(createdAt);
    }
}
