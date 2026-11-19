using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.DispatchPauseTests;

public sealed class HostPauses
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 7, 39, 0, TimeSpan.Zero);

    [Fact]
    public void WhenCreated_HasNoHostPauses()
    {
        DispatchPause.Create().HostPauses.ShouldBeEmpty();
    }

    [Fact]
    public void PauseHost_WhenHostIsNotPaused_AddsAFreshHostPause()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();

        // Act
        bool changed = pause.PauseHost("gitlab.com", Now);

        // Assert
        changed.ShouldBeTrue();
        pause.HostPauses.ShouldBe([HostPause.Start("gitlab.com", Now)]);
    }

    [Fact]
    public void PauseHost_WhenHostIsAlreadyPaused_ChangesNothing()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.PauseHost("gitlab.com", Now);
        pause.RecordProbeUnreachable("gitlab.com", Now.AddMinutes(1));
        IReadOnlyList<HostPause> before = pause.HostPauses;

        // Act
        bool changed = pause.PauseHost("GITLAB.com", Now.AddMinutes(2));

        // Assert
        changed.ShouldBeFalse();
        pause.HostPauses.ShouldBe(before);
    }

    [Fact]
    public void PauseHost_ForAnotherHost_KeepsTheFirstHostsPause()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.PauseHost("gitlab.com", Now);

        // Act
        pause.PauseHost("github.com", Now.AddMinutes(3));

        // Assert
        pause.HostPauses.Select(p => p.Host).ShouldBe(["gitlab.com", "github.com"]);
    }

    [Fact]
    public void RecordProbeUnreachable_AdvancesThatHostsPause()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.PauseHost("gitlab.com", Now);
        pause.PauseHost("github.com", Now);

        // Act
        bool changed = pause.RecordProbeUnreachable("gitlab.com", Now.AddMinutes(1));

        // Assert
        changed.ShouldBeTrue();
        pause.HostPauses.ShouldBe(
        [
            HostPause.Start("gitlab.com", Now).AfterUnreachableProbe(Now.AddMinutes(1)),
            HostPause.Start("github.com", Now),
        ]);
    }

    [Fact]
    public void RecordProbeUnreachable_WhenHostIsNotPaused_ChangesNothing()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();

        // Act
        bool changed = pause.RecordProbeUnreachable("gitlab.com", Now);

        // Assert
        changed.ShouldBeFalse();
        pause.HostPauses.ShouldBeEmpty();
    }

    [Fact]
    public void ClearHostPause_RemovesOnlyThatHost()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.PauseHost("gitlab.com", Now);
        pause.PauseHost("github.com", Now);

        // Act
        bool changed = pause.ClearHostPause("gitlab.com");

        // Assert
        changed.ShouldBeTrue();
        pause.HostPauses.Select(p => p.Host).ShouldBe(["github.com"]);
    }

    [Fact]
    public void ClearHostPause_WhenHostIsNotPaused_ChangesNothing()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.PauseHost("github.com", Now);

        // Act
        bool changed = pause.ClearHostPause("gitlab.com");

        // Assert
        changed.ShouldBeFalse();
        pause.HostPauses.Count.ShouldBe(1);
    }
}
