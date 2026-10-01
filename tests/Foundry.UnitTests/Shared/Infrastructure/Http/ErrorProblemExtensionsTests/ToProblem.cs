using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Http.ErrorProblemExtensionsTests;

public sealed class ToProblem
{
    private static readonly Error TestError = new("TestCode", "Something went wrong.");

    [Fact]
    public void WhenCalled_TypeIsTagUriWithCodeAppended()
    {
        // Arrange
        Error error = new("my.code", "detail text");

        // Act
        ProblemHttpResult result = error.ToProblem(400);

        // Assert
        result.ProblemDetails.Type.ShouldBe("tag:foundry,2026:problems/my.code");
    }

    [Fact]
    public void WhenCalled_DetailIsErrorMessage()
    {
        // Arrange
        Error error = new("SomeCode", "A human-readable message.");

        // Act
        ProblemHttpResult result = error.ToProblem(404);

        // Assert
        result.ProblemDetails.Detail.ShouldBe("A human-readable message.");
    }

    [Fact]
    public void WhenCalled_StatusCodeMatchesSuppliedValue()
    {
        // Arrange
        Error error = TestError;

        // Act
        ProblemHttpResult result = error.ToProblem(409);

        // Assert
        result.StatusCode.ShouldBe(409);
    }
}
