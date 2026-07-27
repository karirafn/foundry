using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class UpdateLimits
{
    [Fact]
    public void WhenValuesAreValid_UpdatesMaxConcurrent()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateLimits(5, 60);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        settings.MaxConcurrent.ShouldBe(5);
    }

    [Fact]
    public void WhenValuesAreValid_UpdatesTimeoutMinutes()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateLimits(5, 60);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        settings.TimeoutMinutes.ShouldBe(60);
    }

    [Fact]
    public void WhenValuesAreValid_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.UpdateLimits(5, 60);

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-1)]
    public void WhenMaxConcurrentIsOutOfRange_ReturnsInvalidMaxConcurrentError(int maxConcurrent)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateLimits(maxConcurrent, 60);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("Settings.InvalidMaxConcurrent");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(10)]
    public void WhenMaxConcurrentIsAtBoundary_Succeeds(int maxConcurrent)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateLimits(maxConcurrent, 60);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    [InlineData(-1)]
    public void WhenTimeoutIsOutOfRange_ReturnsInvalidTimeoutError(int timeoutMinutes)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateLimits(5, timeoutMinutes);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("Settings.InvalidTimeout");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1440)]
    [InlineData(120)]
    public void WhenTimeoutIsAtBoundary_Succeeds(int timeoutMinutes)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateLimits(5, timeoutMinutes);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenMaxConcurrentIsInvalid_DoesNotUpdateState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        int originalMax = settings.MaxConcurrent;

        // Act
        settings.UpdateLimits(0, 60);

        // Assert
        settings.MaxConcurrent.ShouldBe(originalMax);
    }

    [Fact]
    public void WhenTimeoutIsInvalid_DoesNotUpdateState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        int originalTimeout = settings.TimeoutMinutes;

        // Act
        settings.UpdateLimits(5, 0);

        // Assert
        settings.TimeoutMinutes.ShouldBe(originalTimeout);
    }
}
