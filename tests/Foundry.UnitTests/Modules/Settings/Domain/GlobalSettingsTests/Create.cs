using Foundry.Modules.Settings.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_HasDefaultId()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.Id.ShouldBe(GlobalSettingsId.Default);
    }

    [Fact]
    public void WhenCreated_HasDefaultMaxConcurrent()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.MaxConcurrent.ShouldBe(3);
    }

    [Fact]
    public void WhenCreated_HasDefaultTimeoutMinutes()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.TimeoutMinutes.ShouldBe(120);
    }

    [Fact]
    public void WhenCreated_AuthModeIsApiKeyWithEmptyKey()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        AuthMode.ApiKey apiKey = settings.AuthMode.ShouldBeOfType<AuthMode.ApiKey>();
        apiKey.Key.ShouldBe(string.Empty);
    }

    [Fact]
    public void WhenCreated_CreatedAtIsSet()
    {
        // Arrange
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        settings.CreatedAt.ShouldBeInRange(before, after);
    }

    [Fact]
    public void WhenCreated_UpdatedAtMatchesCreatedAt()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.UpdatedAt.ShouldBe(settings.CreatedAt);
    }
}
