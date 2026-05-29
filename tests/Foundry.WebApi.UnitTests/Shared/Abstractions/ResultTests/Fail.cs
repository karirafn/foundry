using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ResultTests;

public sealed class Fail
{
    [Fact]
    public void WhenCalled_ReturnsFailureContainingError()
    {
        // Arrange
        Error error = new("Test.Code", "Test message");

        // Act
        Result<string>.Failure failure = Result<string>.Fail(error).ShouldBeOfType<Result<string>.Failure>();

        // Assert
        failure.Error.ShouldBe(error);
    }
}
