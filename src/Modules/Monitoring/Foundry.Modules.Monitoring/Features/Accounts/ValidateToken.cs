using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class ValidateToken
{
    internal sealed record Query(string Token, Uri ApiBaseUrl, string ProviderType = "github") : IQuery<Response>;

    internal sealed record Response(
        bool IsValid,
        bool IsAuthFailure,
        IReadOnlyList<string> MissingScopes);

    internal sealed class Handler(
        GitHubHttpClient gitHubHttpClient,
        GitLabHttpClient gitLabHttpClient)
        : IQueryHandler<Query, Response>
    {
        private const string GitLabProviderType = "gitlab";

        public async Task<Result<Response>> HandleAsync(Query query, CancellationToken cancellationToken)
        {
            Result<TokenValidationResult> result = string.Equals(
                    query.ProviderType, GitLabProviderType, StringComparison.OrdinalIgnoreCase)
                ? await gitLabHttpClient.ValidateTokenAsync(query.ApiBaseUrl, query.Token, cancellationToken)
                : await gitHubHttpClient.ValidateTokenAsync(query.ApiBaseUrl, query.Token, cancellationToken);

            return result.Match(
                validation => Result<Response>.Ok(new Response(
                    IsValid: validation.IsValid,
                    IsAuthFailure: validation.IsAuthFailure,
                    MissingScopes: validation.MissingScopes)),
                error => Result<Response>.Fail(error));
        }
    }

    internal sealed record RequestBody(string Token, string BaseUrl, string ProviderType = "github");

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/validate-token", static async (
                    RequestBody body,
                    IQueryHandler<Query, Response> handler,
                    CancellationToken cancellationToken) =>
                {
                    if (!Uri.TryCreate(body.BaseUrl, UriKind.Absolute, out Uri? baseUrl) ||
                        baseUrl.Scheme is not "https")
                    {
                        return (Results<Ok<Response>, BadRequest<string>>)TypedResults.BadRequest("Invalid base URL.");
                    }

                    Uri apiBaseUrl = string.Equals(body.ProviderType, "gitlab", StringComparison.OrdinalIgnoreCase)
                        ? GitLabAccount.DeriveApiBaseUrl(baseUrl)
                        : GitHubAccount.DeriveApiBaseUrl(baseUrl);

                    Result<Response> result = await handler.HandleAsync(
                        new Query(body.Token, apiBaseUrl, body.ProviderType),
                        cancellationToken);

                    return result.Match<Results<Ok<Response>, BadRequest<string>>>(
                        response => TypedResults.Ok(response),
                        error => TypedResults.BadRequest(error.Message));
                })
                .WithName("ValidateToken")
                .WithSummary("Validates a personal access token for the given provider")
                .Produces<Response>()
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
