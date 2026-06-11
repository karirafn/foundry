using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class ValidateToken
{
    internal sealed record Query(string Token, Uri ApiBaseUrl) : IQuery<Response>;

    internal sealed record Response(
        bool IsValid,
        bool IsAuthFailure,
        IReadOnlyList<string> MissingScopes);

    internal sealed class Handler(GitHubHttpClient gitHubHttpClient) : IQueryHandler<Query, Response>
    {
        public async Task<Result<Response>> HandleAsync(Query query, CancellationToken cancellationToken)
        {
            Result<TokenValidationResult> result = await gitHubHttpClient.ValidateTokenAsync(
                query.ApiBaseUrl,
                query.Token,
                cancellationToken);

            return result.Match(
                validation => Result<Response>.Ok(new Response(
                    IsValid: validation.IsValid,
                    IsAuthFailure: validation.IsAuthFailure,
                    MissingScopes: validation.MissingScopes)),
                error => Result<Response>.Fail(error));
        }
    }

    internal sealed record RequestBody(string Token, string BaseUrl);

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/validate-token", static async (
                    RequestBody body,
                    IQueryHandler<Query, Response> handler,
                    CancellationToken cancellationToken) =>
                {
                    if (!Uri.TryCreate(body.BaseUrl, UriKind.Absolute, out Uri? baseUrl))
                    {
                        return Results.BadRequest("Invalid base URL.") as IResult;
                    }

                    Uri apiBaseUrl = GitHubAccount.DeriveApiBaseUrl(baseUrl);
                    Result<Response> result = await handler.HandleAsync(
                        new Query(body.Token, apiBaseUrl),
                        cancellationToken);

                    return result.Match<IResult>(
                        response => TypedResults.Ok(response),
                        error => TypedResults.BadRequest(error.Message));
                })
                .WithName("ValidateToken")
                .WithSummary("Validates a GitHub personal access token")
                .Produces<Response>()
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
