using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Features.Dispatch;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.Dispatch.UpdateDispatchSettingsTests;

public sealed class ValidateCommand
{
    private readonly UpdateDispatchSettings.Validator _sut = new();

    [Fact]
    public void WhenValuesAreValid_ReturnsSuccess()
    {
        // Arrange
        UpdateDispatchSettings.Command command = new(
            AutoResumeOnUsageReset: true,
            ProbeIntervalMinutes: 30,
            PollIntervalSeconds: 30);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(60)]
    [InlineData(120)]
    public void WhenProbeIntervalIsAtOrAboveMin_ReturnsSuccess(int probeIntervalMinutes)
    {
        // Arrange
        UpdateDispatchSettings.Command command = new(
            AutoResumeOnUsageReset: true,
            ProbeIntervalMinutes: probeIntervalMinutes,
            PollIntervalSeconds: 30);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void WhenProbeIntervalIsBelowMin_ReturnsInvalidProbeIntervalError(int probeIntervalMinutes)
    {
        // Arrange
        UpdateDispatchSettings.Command command = new(
            AutoResumeOnUsageReset: true,
            ProbeIntervalMinutes: probeIntervalMinutes,
            PollIntervalSeconds: 30);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.InvalidProbeIntervalCode);
    }

    [Theory]
    [InlineData(10081)]
    [InlineData(99999)]
    public void WhenProbeIntervalExceedsMax_ReturnsInvalidProbeIntervalError(int probeIntervalMinutes)
    {
        // Arrange
        UpdateDispatchSettings.Command command = new(
            AutoResumeOnUsageReset: true,
            ProbeIntervalMinutes: probeIntervalMinutes,
            PollIntervalSeconds: 30);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.InvalidProbeIntervalCode);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(3600)]
    public void WhenPollIntervalIsAtOrAboveMinAndAtOrBelowMax_ReturnsSuccess(int pollIntervalSeconds)
    {
        // Arrange
        UpdateDispatchSettings.Command command = new(
            AutoResumeOnUsageReset: true,
            ProbeIntervalMinutes: 30,
            PollIntervalSeconds: pollIntervalSeconds);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void WhenPollIntervalIsBelowMin_ReturnsInvalidPollIntervalError(int pollIntervalSeconds)
    {
        // Arrange
        UpdateDispatchSettings.Command command = new(
            AutoResumeOnUsageReset: true,
            ProbeIntervalMinutes: 30,
            PollIntervalSeconds: pollIntervalSeconds);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.InvalidPollIntervalCode);
    }

    [Theory]
    [InlineData(3601)]
    [InlineData(99999)]
    public void WhenPollIntervalExceedsMax_ReturnsInvalidPollIntervalError(int pollIntervalSeconds)
    {
        // Arrange
        UpdateDispatchSettings.Command command = new(
            AutoResumeOnUsageReset: true,
            ProbeIntervalMinutes: 30,
            PollIntervalSeconds: pollIntervalSeconds);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.InvalidPollIntervalCode);
    }
}
