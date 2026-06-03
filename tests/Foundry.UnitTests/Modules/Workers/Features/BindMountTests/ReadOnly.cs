using Foundry.Modules.Workers.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.BindMountTests;

public sealed class ReadOnly
{
    [Fact]
    public void WhenCreatedWithTwoPositionalArgs_ReadOnlyDefaultsToFalse()
    {
        // Arrange

        // Act
        BindMount mount = new("/host/path", "/container/path");

        // Assert
        mount.ReadOnly.ShouldBeFalse();
    }

    [Fact]
    public void WhenCreatedWithReadOnlyTrue_ReadOnlyIsTrue()
    {
        // Arrange

        // Act
        BindMount mount = new("/host/path", "/container/path", ReadOnly: true);

        // Assert
        mount.ReadOnly.ShouldBeTrue();
    }
}
