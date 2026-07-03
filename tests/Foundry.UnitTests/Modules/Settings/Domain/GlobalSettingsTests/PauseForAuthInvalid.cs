using Foundry.Modules.Settings.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsTests;

public sealed class PauseForAuthInvalid
{
    [Fact]
    public void WhenCalled_SetsAuthInvalidPauseToTrue()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.PauseForAuthInvalid();

        // Assert
        settings.AuthInvalidPause.ShouldBeTrue();
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.PauseForAuthInvalid();

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenCalled_DoesNotAffectIsDispatchPaused()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.PauseForAuthInvalid();

        // Assert
        settings.IsDispatchPaused.ShouldBeFalse();
    }

    [Fact]
    public void WhenCalled_DoesNotAffectUsageLimitResetsAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.PauseForAuthInvalid();

        // Assert
        settings.UsageLimitResetsAt.ShouldBeNull();
    }
}
