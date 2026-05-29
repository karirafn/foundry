using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ResultTests;

public sealed class ImplicitConversion
{
    [Fact]
    public void WhenConvertedFromValue_ProducesSuccessContainingValue()
    {
        // Arrange
        string value = "hello";

        // Act
        Result<string>.Success success = ((Result<string>)value).ShouldBeOfType<Result<string>.Success>();

        // Assert
        success.Value.ShouldBe(value);
    }
}
