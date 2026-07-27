using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class UpdateDispatchSettings
{
    [Fact]
    public void WhenValuesAreValid_UpdatesAutoResumeOnUsageReset()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateDispatchSettings(false, 30);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        settings.AutoResumeOnUsageReset.ShouldBeFalse();
    }

    [Fact]
    public void WhenValuesAreValid_UpdatesDefaultCooldownMinutes()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateDispatchSettings(true, 30);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        settings.DefaultCooldownMinutes.ShouldBe(30);
    }

    [Fact]
    public void WhenValuesAreValid_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.UpdateDispatchSettings(true, 60);

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    [InlineData(-1)]
    public void WhenDefaultCooldownIsOutOfRange_ReturnsInvalidDefaultCooldownError(int cooldownMinutes)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateDispatchSettings(true, cooldownMinutes);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("Settings.InvalidDefaultCooldown");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1440)]
    [InlineData(60)]
    public void WhenDefaultCooldownIsAtBoundary_Succeeds(int cooldownMinutes)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateDispatchSettings(true, cooldownMinutes);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenDefaultCooldownIsInvalid_DoesNotUpdateState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        int originalCooldown = settings.DefaultCooldownMinutes;

        // Act
        settings.UpdateDispatchSettings(true, 0);

        // Assert
        settings.DefaultCooldownMinutes.ShouldBe(originalCooldown);
    }
}
