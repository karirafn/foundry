using Foundry.Modules.Workers.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.VolumeMountTests;

public sealed class ReadOnly
{
    [Fact]
    public void WhenCreatedWithTwoPositionalArgs_ReadOnlyDefaultsToFalse()
    {
        // Arrange

        // Act
        VolumeMount mount = new("foundry-claude-credentials", "/home/node/.claude");

        // Assert
        mount.ReadOnly.ShouldBeFalse();
    }

    [Fact]
    public void WhenCreatedWithReadOnlyTrue_ReadOnlyIsTrue()
    {
        // Arrange

        // Act
        VolumeMount mount = new("foundry-claude-credentials", "/home/node/.claude", ReadOnly: true);

        // Assert
        mount.ReadOnly.ShouldBeTrue();
    }
}
