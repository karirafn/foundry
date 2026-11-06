using Foundry.Modules.Workers.Features.ContainerSpec;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.WorkerContainerSpecTests;

public sealed class Capabilities
{
    [Fact]
    public void WhenCreatedWithDefaultInit_CapabilitiesIsEmpty()
    {
        // Arrange

        // Act
        WorkerContainerSpec spec = new(
            Image: "test-image:latest",
            EnvironmentVariables: new Dictionary<string, string>(),
            Labels: new Dictionary<string, string>(),
            Command: [])
        {
            MemoryLimitMb = 512,
            CpuLimit = 1.0,
            PidsLimit = 256,
        };

        // Assert
        spec.Capabilities.ShouldBeEmpty();
    }

    [Fact]
    public void WhenCreatedWithCapabilities_CapabilitiesContainsTheEntry()
    {
        // Arrange

        // Act
        WorkerContainerSpec spec = new(
            Image: "test-image:latest",
            EnvironmentVariables: new Dictionary<string, string>(),
            Labels: new Dictionary<string, string>(),
            Command: [])
        {
            MemoryLimitMb = 512,
            CpuLimit = 1.0,
            PidsLimit = 256,
            Capabilities = ["SYS_ADMIN"],
        };

        // Assert
        spec.Capabilities.ShouldBe(["SYS_ADMIN"]);
    }
}
