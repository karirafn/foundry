using Foundry.Modules.Workers.Features.ContainerSpec;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.WorkerContainerSpecTests;

public sealed class VolumeMounts
{
    [Fact]
    public void WhenCreatedWithDefaultInit_VolumeMountsIsEmpty()
    {
        // Arrange

        // Act
        WorkerContainerSpec spec = new(
            Image: "test-image:latest",
            EnvironmentVariables: new Dictionary<string, string>(),
            BindMounts: [],
            Labels: new Dictionary<string, string>(),
            Command: [])
        {
            MemoryLimitMb = 512,
            CpuLimit = 1.0,
            PidsLimit = 256,
        };

        // Assert
        spec.VolumeMounts.ShouldBeEmpty();
    }

    [Fact]
    public void WhenCreatedWithVolumeMounts_VolumeMountsContainsTheMount()
    {
        // Arrange
        VolumeMount credentialMount = new("foundry-claude-credentials", "/home/node/.claude", ReadOnly: false);

        // Act
        WorkerContainerSpec spec = new(
            Image: "test-image:latest",
            EnvironmentVariables: new Dictionary<string, string>(),
            BindMounts: [],
            Labels: new Dictionary<string, string>(),
            Command: [])
        {
            MemoryLimitMb = 512,
            CpuLimit = 1.0,
            PidsLimit = 256,
            VolumeMounts = [credentialMount],
        };

        // Assert
        spec.VolumeMounts.ShouldHaveSingleItem();
        spec.VolumeMounts[0].ShouldBe(credentialMount);
    }
}
