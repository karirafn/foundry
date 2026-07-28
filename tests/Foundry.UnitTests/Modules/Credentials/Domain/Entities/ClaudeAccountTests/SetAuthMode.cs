using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Domain.Entities.ClaudeAccountTests;

public sealed class SetAuthMode
{
    [Fact]
    public void WhenSetToApiKey_AuthModeIsApiKey()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        AuthMode.ApiKey apiKey = new("encrypted-key");

        // Act
        account.SetAuthMode(apiKey);

        // Assert
        account.AuthMode.ShouldBe(apiKey);
    }

    [Fact]
    public void WhenSetToApiKey_ValidityIsValid()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();

        // Act
        account.SetAuthMode(new AuthMode.ApiKey("encrypted-key"));

        // Assert
        account.Validity.ShouldBeOfType<CredentialValidity.Valid>();
    }

    [Fact]
    public void WhenSetToApiKey_ClearsOAuthEmail()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");

        // Act
        account.SetAuthMode(new AuthMode.ApiKey("encrypted-key"));

        // Assert
        account.OAuthAccountEmail.ShouldBeNull();
    }

    [Fact]
    public void WhenSetToApiKey_ClearsOAuthOrgName()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");

        // Act
        account.SetAuthMode(new AuthMode.ApiKey("encrypted-key"));

        // Assert
        account.OAuthAccountOrgName.ShouldBeNull();
    }

    [Fact]
    public void WhenSetToOAuth_AuthModeIsOAuth()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        AuthMode.OAuth oauth = new("pro");

        // Act
        account.SetAuthMode(oauth);

        // Assert
        account.AuthMode.ShouldBe(oauth);
    }

    [Fact]
    public void WhenSetAuthMode_UpdatesUpdatedAt()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        DateTimeOffset before = account.UpdatedAt;

        // Act
        account.SetAuthMode(new AuthMode.ApiKey("new-key"));

        // Assert
        account.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenSetAuthMode_DoesNotChangeCreatedAt()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        DateTimeOffset createdAt = account.CreatedAt;

        // Act
        account.SetAuthMode(new AuthMode.ApiKey("new-key"));

        // Assert
        account.CreatedAt.ShouldBe(createdAt);
    }
}
