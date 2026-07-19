using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.NamespaceTests;

public sealed class ToStringMethod
{
    [Fact]
    public void WhenSingleSegment_ReturnsValue()
    {
        // Arrange
        Namespace ns = Namespace.Create("efla").ValueOrThrow();

        // Act
        string result = ns.ToString();

        // Assert
        result.ShouldBe("efla");
    }

    [Fact]
    public void WhenMultiSegment_ReturnsFullPath()
    {
        // Arrange
        Namespace ns = Namespace.Create("efla/databridge").ValueOrThrow();

        // Act
        string result = ns.ToString();

        // Assert
        result.ShouldBe("efla/databridge");
    }
}
