using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class UpdatePollInterval
{
    [Fact]
    public void WhenCreated_PollIntervalSecondsIsDefault()
    {
        // Arrange / Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.PollIntervalSeconds.ShouldBe(GlobalSettings.DefaultPollIntervalSeconds);
    }

    [Fact]
    public void WhenValueIsValid_UpdatesPollIntervalSeconds()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdatePollInterval(60);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        settings.PollIntervalSeconds.ShouldBe(60);
    }

    [Fact]
    public void WhenValueIsValid_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.UpdatePollInterval(60);

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void WhenValueIsBelowMin_ReturnsInvalidPollIntervalError(int pollIntervalSeconds)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdatePollInterval(pollIntervalSeconds);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("Settings.InvalidPollInterval");
    }

    [Fact]
    public void WhenValueIsBelowMin_DoesNotUpdateState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        int originalInterval = settings.PollIntervalSeconds;

        // Act
        settings.UpdatePollInterval(1);

        // Assert
        settings.PollIntervalSeconds.ShouldBe(originalInterval);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(3600)]
    public void WhenValueIsAtOrAboveMinAndAtOrBelowMax_Succeeds(int pollIntervalSeconds)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdatePollInterval(pollIntervalSeconds);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(3601)]
    [InlineData(99999)]
    public void WhenValueExceedsMax_ReturnsInvalidPollIntervalError(int pollIntervalSeconds)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdatePollInterval(pollIntervalSeconds);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("Settings.InvalidPollInterval");
    }

    [Fact]
    public void WhenValueExceedsMax_DoesNotUpdateState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        int originalInterval = settings.PollIntervalSeconds;

        // Act
        settings.UpdatePollInterval(GlobalSettings.MaxPollIntervalSeconds + 1);

        // Assert
        settings.PollIntervalSeconds.ShouldBe(originalInterval);
    }
}
