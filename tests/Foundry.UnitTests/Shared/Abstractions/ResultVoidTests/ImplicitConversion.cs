using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Abstractions.ResultVoidTests;

public sealed class ImplicitConversion
{
    [Fact]
    public void WhenConvertedFromError_ProducesFailureContainingError()
    {
        // Arrange
        Error error = new("Test.Code", "Test message");

        // Act
        Result.Failure failure = ((Result)error).ShouldBeOfType<Result.Failure>();

        // Assert
        failure.Error.ShouldBe(error);
    }
}
