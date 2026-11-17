using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class UpdateAwaitingInputReminderDelay
{
    [Fact]
    public void WhenCreated_ReminderDelayIsThreeDays()
    {
        // Arrange / Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.AwaitingInputReminderDelayHours.ShouldBe(72);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(GlobalSettings.MaxAwaitingInputReminderDelayHours)]
    public void WhenValueIsPositiveAndWithinMax_UpdatesTheDelay(int hours)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateAwaitingInputReminderDelay(hours);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        settings.AwaitingInputReminderDelayHours.ShouldBe(hours);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(GlobalSettings.MaxAwaitingInputReminderDelayHours + 1)]
    public void WhenValueIsNotPositiveOrExceedsMax_ReturnsInvalidReminderDelayAndKeepsTheDelay(int hours)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateAwaitingInputReminderDelay(hours);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe("Settings.InvalidAwaitingInputReminderDelay");
        settings.AwaitingInputReminderDelayHours.ShouldBe(GlobalSettings.DefaultAwaitingInputReminderDelayHours);
    }
}
