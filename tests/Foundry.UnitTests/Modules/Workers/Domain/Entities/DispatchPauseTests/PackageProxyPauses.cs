using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.DispatchPauseTests;

public sealed class PackageProxyPauses
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 7, 39, 0, TimeSpan.Zero);

    [Fact]
    public void WhenCreated_HasNoPackageProxyPause()
    {
        DispatchPause.Create().PackageProxyPause.ShouldBeNull();
    }

    [Fact]
    public void PausePackageProxy_WhenNotPaused_AddsAFreshPause()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();

        // Act
        bool changed = pause.PausePackageProxy(Now);

        // Assert
        changed.ShouldBeTrue();
        pause.PackageProxyPause.ShouldBe(PackageProxyPause.Start(Now));
    }

    [Fact]
    public void PausePackageProxy_WhenAlreadyPaused_KeepsItsBackoff()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.PausePackageProxy(Now);
        pause.RecordPackageProxyProbeUnreachable(Now.AddMinutes(1));
        PackageProxyPause? before = pause.PackageProxyPause;

        // Act
        bool changed = pause.PausePackageProxy(Now.AddMinutes(2));

        // Assert
        changed.ShouldBeFalse();
        pause.PackageProxyPause.ShouldBe(before);
    }

    [Fact]
    public void RecordPackageProxyProbeUnreachable_WhenPaused_AdvancesTheBackoff()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.PausePackageProxy(Now);

        // Act
        bool changed = pause.RecordPackageProxyProbeUnreachable(Now.AddMinutes(1));

        // Assert
        changed.ShouldBeTrue();
        pause.PackageProxyPause.ShouldBe(PackageProxyPause.Start(Now).AfterUnreachableProbe(Now.AddMinutes(1)));
    }

    [Fact]
    public void RecordPackageProxyProbeUnreachable_WhenNotPaused_ChangesNothing()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();

        // Act
        bool changed = pause.RecordPackageProxyProbeUnreachable(Now);

        // Assert
        changed.ShouldBeFalse();
        pause.PackageProxyPause.ShouldBeNull();
    }

    [Fact]
    public void ClearPackageProxyPause_WhenPaused_LiftsIt()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.PausePackageProxy(Now);

        // Act
        bool changed = pause.ClearPackageProxyPause();

        // Assert
        changed.ShouldBeTrue();
        pause.PackageProxyPause.ShouldBeNull();
    }

    [Fact]
    public void ClearPackageProxyPause_WhenNotPaused_ReturnsFalse()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();

        // Act
        bool changed = pause.ClearPackageProxyPause();

        // Assert
        changed.ShouldBeFalse();
    }

    [Fact]
    public void ResumeAllDispatch_WhenThePackageProxyIsPaused_ClearsIt()
    {
        // Arrange
        DispatchPause pause = DispatchPause.Create();
        pause.PausePackageProxy(Now);

        // Act
        pause.ResumeAllDispatch();

        // Assert
        pause.PackageProxyPause.ShouldBeNull();
    }
}
