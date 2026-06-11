using Foundry.Modules.Settings.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsTests;

public sealed class SetAuthMode
{
    [Fact]
    public void WhenSetToApiKey_AuthModeIsApiKey()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        AuthMode.ApiKey newMode = new("my-encrypted-key");

        // Act
        settings.SetAuthMode(newMode);

        // Assert
        settings.AuthMode.ShouldBe(newMode);
    }

    [Fact]
    public void WhenSetToOAuth_AuthModeIsOAuth()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        AuthMode.OAuth oauthMode = new("access", "refresh", DateTimeOffset.UtcNow.AddHours(1), "pro");

        // Act
        settings.SetAuthMode(oauthMode);

        // Assert
        settings.AuthMode.ShouldBe(oauthMode);
    }

    [Fact]
    public void WhenSetAuthMode_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.SetAuthMode(new AuthMode.ApiKey("new-key"));

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenSetAuthMode_DoesNotChangeCreatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset createdAt = settings.CreatedAt;

        // Act
        settings.SetAuthMode(new AuthMode.ApiKey("new-key"));

        // Assert
        settings.CreatedAt.ShouldBe(createdAt);
    }
}
