using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class PackageProxyPauses
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 7, 39, 0, TimeSpan.Zero);

    [Fact]
    public void WhenCreated_HasNoPackageProxyPause()
    {
        GlobalSettings.Create().PackageProxyPause.ShouldBeNull();
    }

    [Fact]
    public void PausePackageProxy_WhenNotPaused_AddsAFreshPause()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        bool changed = settings.PausePackageProxy(Now);

        // Assert
        changed.ShouldBeTrue();
        settings.PackageProxyPause.ShouldBe(PackageProxyPause.Start(Now));
    }

    [Fact]
    public void PausePackageProxy_WhenAlreadyPaused_KeepsItsBackoff()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PausePackageProxy(Now);
        settings.RecordPackageProxyProbeUnreachable(Now.AddMinutes(1));
        PackageProxyPause? before = settings.PackageProxyPause;

        // Act
        bool changed = settings.PausePackageProxy(Now.AddMinutes(2));

        // Assert
        changed.ShouldBeFalse();
        settings.PackageProxyPause.ShouldBe(before);
    }

    [Fact]
    public void RecordPackageProxyProbeUnreachable_WhenPaused_AdvancesTheBackoff()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PausePackageProxy(Now);

        // Act
        bool changed = settings.RecordPackageProxyProbeUnreachable(Now.AddMinutes(1));

        // Assert
        changed.ShouldBeTrue();
        settings.PackageProxyPause.ShouldBe(PackageProxyPause.Start(Now).AfterUnreachableProbe(Now.AddMinutes(1)));
    }

    [Fact]
    public void RecordPackageProxyProbeUnreachable_WhenNotPaused_ChangesNothing()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        bool changed = settings.RecordPackageProxyProbeUnreachable(Now);

        // Assert
        changed.ShouldBeFalse();
        settings.PackageProxyPause.ShouldBeNull();
    }

    [Fact]
    public void ClearPackageProxyPause_WhenPaused_LiftsIt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PausePackageProxy(Now);

        // Act
        bool changed = settings.ClearPackageProxyPause();

        // Assert
        changed.ShouldBeTrue();
        settings.PackageProxyPause.ShouldBeNull();
    }

    [Fact]
    public void ClearPackageProxyPause_WhenNotPaused_ReturnsFalse()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        bool changed = settings.ClearPackageProxyPause();

        // Assert
        changed.ShouldBeFalse();
    }

    [Fact]
    public void ResumeAllDispatch_WhenThePackageProxyIsPaused_ClearsIt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PausePackageProxy(Now);

        // Act
        settings.ResumeAllDispatch();

        // Assert
        settings.PackageProxyPause.ShouldBeNull();
    }
}
