using Foundry.Modules.Workers.Features.ContainerSpec;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.WorkerContainerSpecTests;

public sealed class UnmaskSystemPaths
{
    [Fact]
    public void WhenCreatedWithDefaultInit_UnmaskSystemPathsIsFalse()
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
        spec.UnmaskSystemPaths.ShouldBeFalse();
    }

    [Fact]
    public void WhenCreatedWithUnmaskSystemPaths_UnmaskSystemPathsIsTrue()
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
            UnmaskSystemPaths = true,
        };

        // Assert
        spec.UnmaskSystemPaths.ShouldBeTrue();
    }
}
