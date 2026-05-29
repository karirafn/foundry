using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ResultTests;

public sealed class Match
{
    [Fact]
    public void WhenSuccess_InvokesOnSuccess()
    {
        // Arrange
        Result<string> result = Result<string>.Ok("hello");

        // Act
        string outcome = result.Match(
            onSuccess: value => $"ok:{value}",
            onFailure: error => $"fail:{error.Code}");

        // Assert
        outcome.ShouldBe("ok:hello");
    }

    [Fact]
    public void WhenFailure_InvokesOnFailure()
    {
        // Arrange
        Error error = new("Test.Code", "Test message");
        Result<string> result = Result<string>.Fail(error);

        // Act
        string outcome = result.Match(
            onSuccess: value => $"ok:{value}",
            onFailure: e => $"fail:{e.Code}");

        // Assert
        outcome.ShouldBe("fail:Test.Code");
    }
}
