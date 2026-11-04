using Foundry.Modules.Workers.Features.ContainerSpec;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.RootlessDindRunSpecTests;

public sealed class AllowanceSet
{
    [Fact]
    public void Capabilities_AreExactlyTheRootPhaseAndUidMapNeeds()
    {
        // Arrange

        // Act
        IReadOnlyList<string> capabilities = RootlessDindRunSpec.Capabilities;

        // Assert
        capabilities.ShouldBe(["SETUID", "SETGID", "DAC_OVERRIDE", "SYS_ADMIN"], ignoreOrder: true);
    }

    [Fact]
    public void Capabilities_ExtendTheHardenedSet()
    {
        // Arrange

        // Act
        IReadOnlyList<string> capabilities = RootlessDindRunSpec.Capabilities;

        // Assert
        WorkerHardeningSpec.Capabilities.ShouldBeSubsetOf(capabilities);
    }

    [Fact]
    public void WhenRead_SecurityOptionsAreExactlySeccompAndApparmorUnconfined()
    {
        // Arrange

        // Act
        IReadOnlyList<string> securityOptions = RootlessDindRunSpec.SecurityOptions;

        // Assert
        securityOptions.ShouldBe(["seccomp=unconfined", "apparmor=unconfined"], ignoreOrder: false);
    }

    [Fact]
    public void WhenRead_UnmaskSystemPathsIsTrue()
    {
        // Arrange

        // Act
        bool unmaskSystemPaths = RootlessDindRunSpec.UnmaskSystemPaths;

        // Assert
        unmaskSystemPaths.ShouldBeTrue();
    }

    [Fact]
    public void Devices_ContainsTunDevice()
    {
        // Arrange

        // Act
        IReadOnlyList<string> devices = RootlessDindRunSpec.Devices;

        // Assert
        devices.ShouldBe(["/dev/net/tun"]);
    }

    [Fact]
    public void Devices_DoesNotContainFuseDevice()
    {
        // Arrange

        // Act
        IReadOnlyList<string> devices = RootlessDindRunSpec.Devices;

        // Assert
        devices.ShouldNotContain("/dev/fuse");
    }
}
