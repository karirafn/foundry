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

    [Theory]
    [InlineData("/tmp")]
    [InlineData("/var/tmp")]
    public void WhenLayeredOnTheHardenedSet_TempDirectoryStaysAScratchVolumeNotATmpfs(string path)
    {
        // Arrange
        WorkerContainerSpec hardened = WorkerHardeningSpec.Apply(new(
            Image: "foundry-worker:local",
            EnvironmentVariables: new Dictionary<string, string>(),
            Labels: new Dictionary<string, string>(),
            Command: ["/entrypoint.sh"])
        {
            MemoryLimitMb = 512,
            CpuLimit = 1.0,
            PidsLimit = 256,
        });

        // Act
        // Composed as IssueClaimedHandler composes it: the hardened baseline, then the DinD relaxations.
        WorkerContainerSpec dind = hardened with
        {
            Capabilities = RootlessDindRunSpec.Capabilities,
            SecurityOptions = RootlessDindRunSpec.SecurityOptions,
            Devices = RootlessDindRunSpec.Devices,
            UnmaskSystemPaths = RootlessDindRunSpec.UnmaskSystemPaths,
        };

        // Assert
        dind.ShouldSatisfyAllConditions(
            () => dind.ScratchVolumePaths.ShouldContain(path),
            () => dind.TmpfsMounts.Keys.ShouldNotContain(path));
    }
}
