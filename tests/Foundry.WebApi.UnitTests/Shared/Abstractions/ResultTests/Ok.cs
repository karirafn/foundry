using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ResultTests;

public sealed class Ok
{
    [Fact]
    public void Ok_ReturnsSuccess_ContainingValue()
    {
        // Arrange
        string value = "hello";

        // Act
        Result<string> result = Result<string>.Ok(value);

        // Assert
        result.ShouldBeOfType<Result<string>.Success>();
    }

    [Fact]
    public void Ok_SuccessContainsSuppliedValue()
    {
        // Arrange
        string value = "hello";

        // Act
        Result<string>.Success success = (Result<string>.Success)Result<string>.Ok(value);

        // Assert
        success.Value.ShouldBe(value);
    }
}
