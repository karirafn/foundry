using Foundry.Modules.Settings.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class PauseDispatch
{
    [Fact]
    public void WhenCalled_SetsIsDispatchPausedToTrue()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.PauseDispatch();

        // Assert
        settings.IsDispatchPaused.ShouldBeTrue();
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.PauseDispatch();

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }
}
