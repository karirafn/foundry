using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ResultVoidTests;

public sealed class ImplicitConversion
{
    [Fact]
    public void ImplicitConversion_FromError_ProducesFailure()
    {
        // Arrange
        Error error = new("Test.Code", "Test message");

        // Act
        Result result = error;

        // Assert
        result.ShouldBeOfType<Result.Failure>();
    }

    [Fact]
    public void ImplicitConversion_FromError_FailureContainsSuppliedError()
    {
        // Arrange
        Error error = new("Test.Code", "Test message");

        // Act
        Result result = error;
        Result.Failure failure = (Result.Failure)result;

        // Assert
        failure.Error.ShouldBe(error);
    }
}
