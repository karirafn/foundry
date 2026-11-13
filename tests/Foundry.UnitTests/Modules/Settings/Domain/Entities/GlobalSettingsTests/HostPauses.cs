using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class HostPauses
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 7, 39, 0, TimeSpan.Zero);

    [Fact]
    public void WhenCreated_HasNoHostPauses()
    {
        GlobalSettings.Create().HostPauses.ShouldBeEmpty();
    }

    [Fact]
    public void PauseHost_WhenHostIsNotPaused_AddsAFreshHostPause()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        bool changed = settings.PauseHost("gitlab.com", Now);

        // Assert
        changed.ShouldBeTrue();
        settings.HostPauses.ShouldBe([HostPause.Start("gitlab.com", Now)]);
    }

    [Fact]
    public void PauseHost_WhenHostIsAlreadyPaused_ChangesNothing()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseHost("gitlab.com", Now);
        settings.RecordProbeUnreachable("gitlab.com", Now.AddMinutes(1));
        IReadOnlyList<HostPause> before = settings.HostPauses;

        // Act
        bool changed = settings.PauseHost("GITLAB.com", Now.AddMinutes(2));

        // Assert
        changed.ShouldBeFalse();
        settings.HostPauses.ShouldBe(before);
    }

    [Fact]
    public void PauseHost_ForAnotherHost_KeepsTheFirstHostsPause()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseHost("gitlab.com", Now);

        // Act
        settings.PauseHost("github.com", Now.AddMinutes(3));

        // Assert
        settings.HostPauses.Select(p => p.Host).ShouldBe(["gitlab.com", "github.com"]);
    }

    [Fact]
    public void RecordProbeUnreachable_AdvancesThatHostsPause()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseHost("gitlab.com", Now);
        settings.PauseHost("github.com", Now);

        // Act
        bool changed = settings.RecordProbeUnreachable("gitlab.com", Now.AddMinutes(1));

        // Assert
        changed.ShouldBeTrue();
        settings.HostPauses.ShouldBe(
        [
            HostPause.Start("gitlab.com", Now).AfterUnreachableProbe(Now.AddMinutes(1)),
            HostPause.Start("github.com", Now),
        ]);
    }

    [Fact]
    public void RecordProbeUnreachable_WhenHostIsNotPaused_ChangesNothing()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        bool changed = settings.RecordProbeUnreachable("gitlab.com", Now);

        // Assert
        changed.ShouldBeFalse();
        settings.HostPauses.ShouldBeEmpty();
    }

    [Fact]
    public void ClearHostPause_RemovesOnlyThatHost()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseHost("gitlab.com", Now);
        settings.PauseHost("github.com", Now);

        // Act
        bool changed = settings.ClearHostPause("gitlab.com");

        // Assert
        changed.ShouldBeTrue();
        settings.HostPauses.Select(p => p.Host).ShouldBe(["github.com"]);
    }

    [Fact]
    public void ClearHostPause_WhenHostIsNotPaused_ChangesNothing()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseHost("github.com", Now);

        // Act
        bool changed = settings.ClearHostPause("gitlab.com");

        // Assert
        changed.ShouldBeFalse();
        settings.HostPauses.Count.ShouldBe(1);
    }
}
