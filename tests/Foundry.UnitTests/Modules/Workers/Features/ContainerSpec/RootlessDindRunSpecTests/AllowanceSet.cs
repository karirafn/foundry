using Foundry.Modules.Workers.Features.ContainerSpec;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.RootlessDindRunSpecTests;

public sealed class AllowanceSet
{
    [Fact]
    public void Capabilities_ContainsSysAdmin()
    {
        // Arrange

        // Act
        IReadOnlyList<string> capabilities = RootlessDindRunSpec.Capabilities;

        // Assert
        capabilities.ShouldBe(["SYS_ADMIN"]);
    }

    [Fact]
    public void SecurityOptions_ContainsRequiredUnconfinedOptions()
    {
        // Arrange

        // Act
        IReadOnlyList<string> securityOptions = RootlessDindRunSpec.SecurityOptions;

        // Assert
        securityOptions.ShouldSatisfyAllConditions(
            () => securityOptions.ShouldContain("seccomp=unconfined"),
            () => securityOptions.ShouldContain("apparmor=unconfined"),
            () => securityOptions.ShouldContain("systempaths=unconfined")
        );
    }

    [Fact]
    public void SecurityOptions_ContainsExactlyThreeOptions()
    {
        // Arrange

        // Act
        IReadOnlyList<string> securityOptions = RootlessDindRunSpec.SecurityOptions;

        // Assert
        securityOptions.Count.ShouldBe(3);
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
