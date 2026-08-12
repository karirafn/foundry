using Foundry.Modules.Settings.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class ClearUsageLimitPause
{
    [Fact]
    public void WhenCalled_ClearsUsageLimitResetsAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        settings.ClearUsageLimitPause();

        // Assert
        settings.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.ClearUsageLimitPause();

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenManualPauseNotSet_ReturnsTrue()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        bool result = settings.ClearUsageLimitPause();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenManualPauseIsSet_ReturnsFalse()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseDispatch();
        settings.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        bool result = settings.ClearUsageLimitPause();

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenManualPauseIsSet_PreservesIsDispatchPaused()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseDispatch();
        settings.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        settings.ClearUsageLimitPause();

        // Assert
        settings.IsDispatchPaused.ShouldBeTrue();
    }
}
