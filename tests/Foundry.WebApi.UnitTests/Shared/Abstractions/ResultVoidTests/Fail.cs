using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ResultVoidTests;

public sealed class Fail
{
    [Fact]
    public void Fail_ReturnsFailure()
    {
        // Arrange
        Error error = new("Test.Code", "Test message");

        // Act
        Result result = Result.Fail(error);

        // Assert
        result.ShouldBeOfType<Result.Failure>();
    }

    [Fact]
    public void Fail_FailureContainsSuppliedError()
    {
        // Arrange
        Error error = new("Test.Code", "Test message");

        // Act
        Result.Failure failure = (Result.Failure)Result.Fail(error);

        // Assert
        failure.Error.ShouldBe(error);
    }
}
