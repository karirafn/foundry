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
        UpdateDispatchSettings.Command command = new(AutoResumeOnUsageReset: true, DefaultCooldownMinutes: 60);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(720)]
    [InlineData(1440)]
    public void WhenDefaultCooldownMinutesIsAtBoundary_ReturnsSuccess(int minutes)
    {
        // Arrange
        UpdateDispatchSettings.Command command = new(AutoResumeOnUsageReset: false, DefaultCooldownMinutes: minutes);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    [InlineData(-1)]
    public void WhenDefaultCooldownMinutesIsOutOfRange_ReturnsInvalidDefaultCooldownError(int minutes)
    {
        // Arrange
        UpdateDispatchSettings.Command command = new(AutoResumeOnUsageReset: true, DefaultCooldownMinutes: minutes);

        // Act
        Result result = _sut.Validate(command);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.InvalidDefaultCooldownCode);
    }
}
