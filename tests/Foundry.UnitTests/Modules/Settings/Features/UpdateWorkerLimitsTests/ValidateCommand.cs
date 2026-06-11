using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.UpdateWorkerLimitsTests;

public sealed class ValidateCommand
{
    private readonly UpdateWorkerLimits.Validator _sut = new();

    [Fact]
    public void WhenValuesAreValid_ReturnsSuccess()
    {
        // Arrange
        UpdateWorkerLimits.Command command = new(5, 60);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(20)]
    public void WhenMaxConcurrentIsAtBoundary_ReturnsSuccess(int maxConcurrent)
    {
        // Arrange
        UpdateWorkerLimits.Command command = new(maxConcurrent, 60);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-1)]
    public void WhenMaxConcurrentIsOutOfRange_ReturnsInvalidMaxConcurrentError(int maxConcurrent)
    {
        // Arrange
        UpdateWorkerLimits.Command command = new(maxConcurrent, 60);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.InvalidMaxConcurrentCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(720)]
    [InlineData(1440)]
    public void WhenTimeoutMinutesIsAtBoundary_ReturnsSuccess(int timeoutMinutes)
    {
        // Arrange
        UpdateWorkerLimits.Command command = new(5, timeoutMinutes);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    [InlineData(-1)]
    public void WhenTimeoutMinutesIsOutOfRange_ReturnsInvalidTimeoutError(int timeoutMinutes)
    {
        // Arrange
        UpdateWorkerLimits.Command command = new(5, timeoutMinutes);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.InvalidTimeoutCode);
    }
}
