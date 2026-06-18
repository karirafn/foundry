using Foundry.Modules.Settings.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsTests;

public sealed class ResumeDispatch
{
    [Fact]
    public void WhenCalled_SetsIsDispatchPausedToFalse()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseDispatch();

        // Act
        settings.ResumeDispatch();

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
        settings.ResumeDispatch();

        // Assert
        settings.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseDispatch();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.ResumeDispatch();

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }
}
