using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ResultTests;

public sealed class IsSuccessIsFailure
{
    [Fact]
    public void IsSuccess_WhenSuccess_ReturnsTrue()
    {
        // Arrange
        Result<string> result = Result<string>.Ok("hello");

        // Act & Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void IsFailure_WhenSuccess_ReturnsFalse()
    {
        // Arrange
        Result<string> result = Result<string>.Ok("hello");

        // Act & Assert
        result.IsFailure.ShouldBeFalse();
    }

    [Fact]
    public void IsFailure_WhenFailure_ReturnsTrue()
    {
        // Arrange
        Error error = new("Test.Code", "Test message");
        Result<string> result = Result<string>.Fail(error);

        // Act & Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void IsSuccess_WhenFailure_ReturnsFalse()
    {
        // Arrange
        Error error = new("Test.Code", "Test message");
        Result<string> result = Result<string>.Fail(error);

        // Act & Assert
        result.IsSuccess.ShouldBeFalse();
    }
}
