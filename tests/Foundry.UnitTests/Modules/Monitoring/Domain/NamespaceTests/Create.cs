using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.NamespaceTests;

public sealed class Create
{
    [Fact]
    public void WhenValueIsValidSingleSegment_ReturnsNamespace()
    {
        // Arrange
        string input = "efla";

        // Act
        Result<Namespace> result = Namespace.Create(input);

        // Assert
        Namespace ns = result.ValueOrThrow();
        ns.Value.ShouldBe("efla");
    }

    [Fact]
    public void WhenValueIsValidMultiSegment_ReturnsNamespace()
    {
        // Arrange
        string input = "efla/databridge";

        // Act
        Result<Namespace> result = Namespace.Create(input);

        // Assert
        Namespace ns = result.ValueOrThrow();
        ns.Value.ShouldBe("efla/databridge");
    }

    [Fact]
    public void WhenValueIsValidThreeSegments_ReturnsNamespace()
    {
        // Arrange
        string input = "efla/databridge/akraborg";

        // Act
        Result<Namespace> result = Namespace.Create(input);

        // Assert
        Namespace ns = result.ValueOrThrow();
        ns.Value.ShouldBe("efla/databridge/akraborg");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    [InlineData("/no-leading")]
    [InlineData("trailing/")]
    [InlineData("double//slash")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("owner/./sub")]
    [InlineData("owner/../sub")]
    [InlineData("owner/./")]
    [InlineData("bad chars!")]
    [InlineData("owner%2Fsub")]
    public void WhenValueIsInvalid_ReturnsFailure(string? input)
    {
        // Arrange

        // Act
        Result<Namespace> result = Namespace.Create(input!);

        // Assert
        result.ShouldBeOfType<Result<Namespace>.Failure>();
    }
}
