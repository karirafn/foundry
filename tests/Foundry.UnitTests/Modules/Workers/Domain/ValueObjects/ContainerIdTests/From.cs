using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ContainerIdTests;

public sealed class From
{
    [Fact]
    public void WhenCalledWithString_ReturnsContainerIdWithSameValue()
    {
        // Arrange
        const string rawId = "abc123def456";

        // Act
        ContainerId containerId = ContainerId.From(rawId);

        // Assert
        containerId.Value.ShouldBe(rawId);
    }

    [Fact]
    public void WhenCalledWithSameString_ProducesEqualIds()
    {
        // Arrange
        const string rawId = "container-abc";

        // Act
        ContainerId a = ContainerId.From(rawId);
        ContainerId b = ContainerId.From(rawId);

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenCalledWithDifferentStrings_ProducesDistinctIds()
    {
        // Arrange

        // Act
        ContainerId a = ContainerId.From("container-1");
        ContainerId b = ContainerId.From("container-2");

        // Assert
        a.ShouldNotBe(b);
    }
}
