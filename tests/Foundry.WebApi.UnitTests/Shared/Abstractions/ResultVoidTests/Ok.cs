using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ResultVoidTests;

public sealed class Ok
{
    [Fact]
    public void WhenCalled_ReturnsSuccess()
    {
        // Arrange

        // Act
        Result result = Result.Ok();

        // Assert
        result.ShouldBeOfType<Result.Success>();
    }
}
