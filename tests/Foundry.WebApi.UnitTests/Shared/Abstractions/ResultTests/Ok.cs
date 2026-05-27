using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ResultTests;

public sealed class Ok
{
    [Fact]
    public void WhenCalled_ReturnsSuccessContainingValue()
    {
        // Arrange
        string value = "hello";

        // Act
        Result<string>.Success success = Result<string>.Ok(value).ShouldBeOfType<Result<string>.Success>();

        // Assert
        success.Value.ShouldBe(value);
    }
}
