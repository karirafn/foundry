using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ResultVoidTests;

public sealed class Match
{
    [Fact]
    public void WhenSuccess_InvokesOnSuccess()
    {
        // Arrange
        Result result = Result.Ok();

        // Act
        string outcome = result.Match(
            onSuccess: () => "ok",
            onFailure: error => $"fail:{error.Code}");

        // Assert
        outcome.ShouldBe("ok");
    }

    [Fact]
    public void WhenFailure_InvokesOnFailure()
    {
        // Arrange
        Error error = new("Test.Code", "Test message");
        Result result = Result.Fail(error);

        // Act
        string outcome = result.Match(
            onSuccess: () => "ok",
            onFailure: e => $"fail:{e.Code}");

        // Assert
        outcome.ShouldBe("fail:Test.Code");
    }
}
