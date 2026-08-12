using Foundry.Modules.Settings.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class ResumeAllDispatch
{
    [Fact]
    public void WhenCalled_SetsIsDispatchPausedToFalse()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseDispatch();

        // Act
        settings.ResumeAllDispatch();

        // Assert
        settings.IsDispatchPaused.ShouldBeFalse();
    }

    [Fact]
    public void WhenCalled_ClearsUsageLimitResetsAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        settings.ResumeAllDispatch();

        // Assert
        settings.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public void WhenCalled_ClearsBothPauseReasons()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseDispatch();
        settings.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        settings.ResumeAllDispatch();

        // Assert
        settings.ShouldSatisfyAllConditions(
            () => settings.IsDispatchPaused.ShouldBeFalse(),
            () => settings.UsageLimitResetsAt.ShouldBeNull());
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseDispatch();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.ResumeAllDispatch();

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }
}
