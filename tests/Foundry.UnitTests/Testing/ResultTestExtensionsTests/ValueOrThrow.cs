using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Testing.ResultTestExtensionsTests;

public sealed class ValueOrThrow
{
    [Fact]
    public void WhenSuccess_ReturnsWrappedValue()
    {
        // Arrange
        Result<string> result = Result<string>.Ok("hello");

        // Act
        string value = result.ValueOrThrow();

        // Assert
        value.ShouldBe("hello");
    }

    [Fact]
    public void WhenFailure_ThrowsInvalidOperationExceptionWithCodeAndMessage()
    {
        // Arrange
        Error error = new("Test.Code", "Something went wrong");
        Result<string> result = Result<string>.Fail(error);

        // Act
        InvalidOperationException ex = Should.Throw<InvalidOperationException>(() => result.ValueOrThrow());

        // Assert
        ex.Message.ShouldBe("Test.Code: Something went wrong");
    }
}
