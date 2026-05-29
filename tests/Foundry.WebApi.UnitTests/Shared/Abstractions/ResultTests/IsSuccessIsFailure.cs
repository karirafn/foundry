using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ResultTests;

public sealed class IsSuccessIsFailure
{
    [Fact]
    public void WhenSuccess_IsSuccessReturnsTrue()
    {
        // Arrange
        Result<string> result = Result<string>.Ok("hello");

        // Act & Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenSuccess_IsFailureReturnsFalse()
    {
        // Arrange
        Result<string> result = Result<string>.Ok("hello");

        // Act & Assert
        result.IsFailure.ShouldBeFalse();
    }

    [Fact]
    public void WhenFailure_IsFailureReturnsTrue()
    {
        // Arrange
        Error error = new("Test.Code", "Test message");
        Result<string> result = Result<string>.Fail(error);

        // Act & Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenFailure_IsSuccessReturnsFalse()
    {
        // Arrange
        Error error = new("Test.Code", "Test message");
        Result<string> result = Result<string>.Fail(error);

        // Act & Assert
        result.IsSuccess.ShouldBeFalse();
    }
}
