using Foundry.Modules.Workers.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.DispatchPauseTests;

public sealed class PauseDispatch
{
    [Fact]
    public void WhenCalled_SetsIsDispatchPausedToTrue()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();

        // Act
        pause.PauseDispatch();

        // Assert
        pause.IsDispatchPaused.ShouldBeTrue();
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        DateTimeOffset before = pause.UpdatedAt;

        // Act
        pause.PauseDispatch();

        // Assert
        pause.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }
}
