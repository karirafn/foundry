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
        UpdateDispatchSettings.Command command = new(AutoResumeOnUsageReset: true, ProbeIntervalMinutes: 30);

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
        UpdateDispatchSettings.Command command = new(AutoResumeOnUsageReset: true, probeIntervalMinutes);

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
        UpdateDispatchSettings.Command command = new(AutoResumeOnUsageReset: true, probeIntervalMinutes);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.InvalidProbeIntervalCode);
    }
}
