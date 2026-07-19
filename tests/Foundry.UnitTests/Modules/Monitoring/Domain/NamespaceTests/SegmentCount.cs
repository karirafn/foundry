using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.NamespaceTests;

public sealed class SegmentCount
{
    [Fact]
    public void WhenSingleSegment_ReturnsOne()
    {
        // Arrange
        Namespace ns = Namespace.Create("efla").ValueOrThrow();

        // Act
        int count = ns.SegmentCount;

        // Assert
        count.ShouldBe(1);
    }

    [Fact]
    public void WhenTwoSegments_ReturnsTwo()
    {
        // Arrange
        Namespace ns = Namespace.Create("efla/databridge").ValueOrThrow();

        // Act
        int count = ns.SegmentCount;

        // Assert
        count.ShouldBe(2);
    }

    [Fact]
    public void WhenThreeSegments_ReturnsThree()
    {
        // Arrange
        Namespace ns = Namespace.Create("efla/databridge/akraborg").ValueOrThrow();

        // Act
        int count = ns.SegmentCount;

        // Assert
        count.ShouldBe(3);
    }
}
