using Foundry.Modules.Settings.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class UpdateDispatchSettings
{
    [Fact]
    public void WhenCalled_UpdatesAutoResumeOnUsageReset()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.UpdateDispatchSettings(false);

        // Assert
        settings.AutoResumeOnUsageReset.ShouldBeFalse();
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.UpdateDispatchSettings(true);

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }
}
