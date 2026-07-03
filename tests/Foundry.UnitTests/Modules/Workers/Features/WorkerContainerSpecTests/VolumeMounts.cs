using Foundry.Modules.Workers.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerContainerSpecTests;

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
            Command: []);

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
            VolumeMounts = [credentialMount],
        };

        // Assert
        spec.VolumeMounts.ShouldHaveSingleItem();
        spec.VolumeMounts[0].ShouldBe(credentialMount);
    }
}
