using Foundry.Modules.Workers.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.DispatchPauseTests;

public sealed class ResumeAllDispatch
{
    [Fact]
    public void WhenCalled_SetsIsDispatchPausedToFalse()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.PauseDispatch();

        // Act
        pause.ResumeAllDispatch();

        // Assert
        pause.IsDispatchPaused.ShouldBeFalse();
    }

    [Fact]
    public void WhenCalled_ClearsUsageLimitResetsAt()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        pause.ResumeAllDispatch();

        // Assert
        pause.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public void WhenCalled_ClearsBothPauseReasons()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.PauseDispatch();
        pause.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        pause.ResumeAllDispatch();

        // Assert
        pause.ShouldSatisfyAllConditions(
            () => pause.IsDispatchPaused.ShouldBeFalse(),
            () => pause.UsageLimitResetsAt.ShouldBeNull());
    }

    [Fact]
    public void WhenHostsArePaused_ClearsEveryHostPause()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.PauseHost("gitlab.com", DateTimeOffset.UtcNow);
        pause.PauseHost("github.com", DateTimeOffset.UtcNow);

        // Act
        pause.ResumeAllDispatch();

        // Assert
        pause.HostPauses.ShouldBeEmpty();
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.PauseDispatch();
        DateTimeOffset before = pause.UpdatedAt;

        // Act
        pause.ResumeAllDispatch();

        // Assert
        pause.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }
}
