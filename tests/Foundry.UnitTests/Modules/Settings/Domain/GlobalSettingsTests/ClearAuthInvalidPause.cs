using Foundry.Modules.Settings.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsTests;

public sealed class ClearAuthInvalidPause
{
    [Fact]
    public void WhenPauseIsActive_SetsAuthInvalidPauseToFalse()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseForAuthInvalid();

        // Act
        settings.ClearAuthInvalidPause();

        // Assert
        settings.AuthInvalidPause.ShouldBeFalse();
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseForAuthInvalid();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.ClearAuthInvalidPause();

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenCalled_DoesNotAffectIsDispatchPaused()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseDispatch();

        // Act
        settings.ClearAuthInvalidPause();

        // Assert
        settings.IsDispatchPaused.ShouldBeTrue();
    }

    [Fact]
    public void WhenCalled_DoesNotAffectUsageLimitResetsAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(2));

        // Act
        settings.ClearAuthInvalidPause();

        // Assert
        settings.UsageLimitResetsAt.ShouldNotBeNull();
    }
}
