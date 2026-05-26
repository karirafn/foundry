using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ResultTests;

public sealed class Fail
{
    [Fact]
    public void Fail_ReturnsFailure()
    {
        // Arrange
        Error error = new("Test.Code", "Test message");

        // Act
        Result<string> result = Result<string>.Fail(error);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>();
    }

    [Fact]
    public void Fail_FailureContainsSuppliedError()
    {
        // Arrange
        Error error = new("Test.Code", "Test message");

        // Act
        Result<string>.Failure failure = (Result<string>.Failure)Result<string>.Fail(error);

        // Assert
        failure.Error.ShouldBe(error);
    }
}
