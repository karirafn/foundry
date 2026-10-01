using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Foundry.Shared.Infrastructure.Http;

public static class ErrorProblemExtensions
{
    private const string TypePrefix = "tag:foundry,2026:problems/";

    public static ProblemHttpResult ToProblem(this Error error, int statusCode) =>
        TypedResults.Problem(
            detail: error.Message,
            statusCode: statusCode,
            type: $"{TypePrefix}{error.Code}");
}
