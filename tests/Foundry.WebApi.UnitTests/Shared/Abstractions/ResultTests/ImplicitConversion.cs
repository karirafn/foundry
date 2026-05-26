using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ResultTests;

public sealed class ImplicitConversion
{
    [Fact]
    public void ImplicitConversion_FromValue_ProducesSuccess()
    {
        // Arrange
        string value = "hello";

        // Act
        Result<string> result = value;

        // Assert
        result.ShouldBeOfType<Result<string>.Success>();
    }

    [Fact]
    public void ImplicitConversion_FromValue_SuccessContainsValue()
    {
        // Arrange
        string value = "hello";

        // Act
        Result<string>.Success success = (Result<string>.Success)(Result<string>)value;

        // Assert
        success.Value.ShouldBe(value);
    }
}
