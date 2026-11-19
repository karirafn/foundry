using Foundry.Modules.Workers.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.DispatchPauseTests;

public sealed class ClearUsageLimitPause
{
    [Fact]
    public void WhenCalled_ClearsUsageLimitResetsAt()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        pause.ClearUsageLimitPause();

        // Assert
        pause.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));
        DateTimeOffset before = pause.UpdatedAt;

        // Act
        pause.ClearUsageLimitPause();

        // Assert
        pause.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenManualPauseNotSet_ReturnsTrue()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        bool result = pause.ClearUsageLimitPause();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenManualPauseIsSet_ReturnsFalse()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.PauseDispatch();
        pause.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        bool result = pause.ClearUsageLimitPause();

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenManualPauseIsSet_PreservesIsDispatchPaused()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.PauseDispatch();
        pause.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        pause.ClearUsageLimitPause();

        // Assert
        pause.IsDispatchPaused.ShouldBeTrue();
    }
}
