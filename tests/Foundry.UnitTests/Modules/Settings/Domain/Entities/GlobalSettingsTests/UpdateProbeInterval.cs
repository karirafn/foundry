using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class UpdateProbeInterval
{
    [Fact]
    public void WhenCreated_ProbeIntervalMinutesIsDefault()
    {
        // Arrange / Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.ProbeIntervalMinutes.ShouldBe(GlobalSettings.DefaultProbeIntervalMinutes);
    }

    [Fact]
    public void WhenValueIsValid_UpdatesProbeIntervalMinutes()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateProbeInterval(30);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        settings.ProbeIntervalMinutes.ShouldBe(30);
    }

    [Fact]
    public void WhenValueIsValid_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.UpdateProbeInterval(30);

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void WhenValueIsBelowMin_ReturnsInvalidProbeIntervalError(int probeIntervalMinutes)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateProbeInterval(probeIntervalMinutes);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("Settings.InvalidProbeInterval");
    }

    [Fact]
    public void WhenValueIsBelowMin_DoesNotUpdateState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        int originalInterval = settings.ProbeIntervalMinutes;

        // Act
        settings.UpdateProbeInterval(1);

        // Assert
        settings.ProbeIntervalMinutes.ShouldBe(originalInterval);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(60)]
    [InlineData(120)]
    public void WhenValueIsAtOrAboveMin_Succeeds(int probeIntervalMinutes)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateProbeInterval(probeIntervalMinutes);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }
}
