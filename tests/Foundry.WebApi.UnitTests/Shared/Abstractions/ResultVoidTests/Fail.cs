using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ResultVoidTests;

public sealed class Fail
{
    [Fact]
    public void WhenCalled_ReturnsFailureContainingError()
    {
        // Arrange
        Error error = new("Test.Code", "Test message");

        // Act
        Result.Failure failure = Result.Fail(error).ShouldBeOfType<Result.Failure>();

        // Assert
        failure.Error.ShouldBe(error);
    }
}
