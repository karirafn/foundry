using Foundry.Modules.Workers.Features.ContainerSpec;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.WorkerHardeningSpecTests;

public sealed class HardeningSet
{
    private static WorkerContainerSpec UnhardenedSpec() =>
        new(
            Image: "foundry-worker:local",
            EnvironmentVariables: new Dictionary<string, string>(),
            BindMounts: [],
            Labels: new Dictionary<string, string>(),
            Command: ["/entrypoint.sh"])
        {
            MemoryLimitMb = 512,
            CpuLimit = 1.0,
            PidsLimit = 256,
            VolumeMounts = [new VolumeMount("foundry-claude-credentials", "/home/node/.claude")],
        };

    [Fact]
    public void WhenRead_DropsEveryCapability()
    {
        // Arrange

        // Act
        IReadOnlyList<string> dropped = WorkerHardeningSpec.DroppedCapabilities;

        // Assert
        dropped.ShouldBe(["ALL"]);
    }

    [Fact]
    public void WhenRead_AddsBackOnlyWhatTheSetprivDropNeeds()
    {
        // Arrange

        // Act
        IReadOnlyList<string> capabilities = WorkerHardeningSpec.Capabilities;

        // Assert
        capabilities.ShouldBe(["SETUID", "SETGID"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("SYS_ADMIN")]
    [InlineData("NET_ADMIN")]
    [InlineData("NET_RAW")]
    [InlineData("SETFCAP")]
    [InlineData("SETPCAP")]
    [InlineData("MKNOD")]
    [InlineData("DAC_OVERRIDE")]
    [InlineData("CHOWN")]
    [InlineData("FOWNER")]
    [InlineData("SYS_CHROOT")]
    public void WhenRead_DoesNotAddDangerousCapability(string capability)
    {
        // Arrange

        // Act
        IReadOnlyList<string> capabilities = WorkerHardeningSpec.Capabilities;

        // Assert
        capabilities.ShouldNotContain(capability);
    }

    [Fact]
    public void WhenRead_SetsNoNewPrivilegesAndKeepsTheDefaultSeccompProfile()
    {
        // Arrange

        // Act
        IReadOnlyList<string> securityOptions = WorkerHardeningSpec.SecurityOptions;

        // Assert
        securityOptions.ShouldSatisfyAllConditions(
            () => securityOptions.ShouldBe(["no-new-privileges:true"]),
            () => securityOptions.ShouldNotContain(o => o.StartsWith("seccomp=", StringComparison.Ordinal)),
            () => securityOptions.ShouldNotContain(o => o.StartsWith("apparmor=", StringComparison.Ordinal)));
    }

    [Fact]
    public void WhenRead_RootFilesystemIsReadOnly()
    {
        // Arrange

        // Act
        bool readOnly = WorkerHardeningSpec.ReadOnlyRootFilesystem;

        // Assert
        readOnly.ShouldBeTrue();
    }

    [Fact]
    public void WhenRead_ManagedSettingsDirectoryIsATmpfsThatOnlyRootCanWrite()
    {
        // Arrange

        // Act
        string options = WorkerHardeningSpec.TmpfsMounts[WorkerHardeningSpec.ManagedSettingsDirectory];

        // Assert
        options.Split(',').ShouldSatisfyAllConditions(
            o => o.ShouldContain("mode=0755"),
            o => o.ShouldContain("noexec"),
            o => o.ShouldContain("nosuid"),
            o => o.ShouldNotContain(x => x.StartsWith("uid=", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("/tmp")]
    [InlineData("/var/tmp")]
    public void WhenRead_TempDirectoriesAreStickyWorldWritableTmpfs(string path)
    {
        // Arrange

        // Act
        string options = WorkerHardeningSpec.TmpfsMounts[path];

        // Assert
        options.Split(',').ShouldSatisfyAllConditions(
            o => o.ShouldContain("mode=1777"),
            o => o.ShouldContain("nosuid"),
            o => o.ShouldContain("exec"));
    }

    [Fact]
    public void WhenRead_CloneAndHomeAreScratchVolumes()
    {
        // Arrange

        // Act
        IReadOnlyList<string> paths = WorkerHardeningSpec.ScratchVolumePaths;

        // Assert
        paths.ShouldBe(["/workspace", "/home/node"], ignoreOrder: true);
    }

    [Fact]
    public void WhenApplied_SetsEveryHardeningField()
    {
        // Arrange
        WorkerContainerSpec spec = UnhardenedSpec();

        // Act
        WorkerContainerSpec hardened = WorkerHardeningSpec.Apply(spec);

        // Assert
        hardened.ShouldSatisfyAllConditions(
            () => hardened.DroppedCapabilities.ShouldBe(WorkerHardeningSpec.DroppedCapabilities),
            () => hardened.Capabilities.ShouldBe(WorkerHardeningSpec.Capabilities),
            () => hardened.SecurityOptions.ShouldBe(WorkerHardeningSpec.SecurityOptions),
            () => hardened.ReadOnlyRootFilesystem.ShouldBeTrue(),
            () => hardened.TmpfsMounts.ShouldBe(WorkerHardeningSpec.TmpfsMounts),
            () => hardened.ScratchVolumePaths.ShouldBe(WorkerHardeningSpec.ScratchVolumePaths));
    }

    [Fact]
    public void WhenApplied_KeepsTheRestOfTheSpec()
    {
        // Arrange
        WorkerContainerSpec spec = UnhardenedSpec();

        // Act
        WorkerContainerSpec hardened = WorkerHardeningSpec.Apply(spec);

        // Assert
        hardened.ShouldSatisfyAllConditions(
            () => hardened.Image.ShouldBe(spec.Image),
            () => hardened.Command.ShouldBe(spec.Command),
            () => hardened.MemoryLimitMb.ShouldBe(spec.MemoryLimitMb),
            () => hardened.VolumeMounts.ShouldBe(spec.VolumeMounts),
            () => hardened.UnmaskSystemPaths.ShouldBeFalse(),
            () => hardened.Devices.ShouldBeEmpty());
    }
}
