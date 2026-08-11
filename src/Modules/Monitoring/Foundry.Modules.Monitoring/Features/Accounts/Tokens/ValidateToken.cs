using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;

using BaseUrlVo = Foundry.Modules.Monitoring.Domain.ValueObjects.BaseUrl;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Monitoring.Features.Accounts.Tokens;

internal static class ValidateToken
{
    internal sealed record Query(string Token, Uri ApiBaseUrl, string ProviderType) : IQuery<Response>;

    internal sealed record Response(
        bool IsValid,
        bool IsAuthFailure,
        bool ScopesVerified,
        IReadOnlyList<string> MissingScopes,
        string? AccountName);

    internal sealed class Handler(
        GitHubHttpClient gitHubHttpClient,
        GitLabHttpClient gitLabHttpClient)
        : IQueryHandler<Query, Response>
    {
        public async Task<Result<Response>> HandleAsync(Query query, CancellationToken cancellationToken)
        {
            Result<TokenValidationResult> result = string.Equals(
                    query.ProviderType, ProviderTypes.GitLab, StringComparison.OrdinalIgnoreCase)
                ? await gitLabHttpClient.ValidateTokenAsync(query.ApiBaseUrl, query.Token, cancellationToken)
                : await gitHubHttpClient.ValidateTokenAsync(query.ApiBaseUrl, query.Token, cancellationToken);

            return result.Match(
                validation => Result<Response>.Ok(new Response(
                    IsValid: validation.IsValid,
                    IsAuthFailure: validation.IsAuthFailure,
                    ScopesVerified: validation.ScopesVerified,
                    MissingScopes: validation.MissingScopes,
                    AccountName: validation.AccountName)),
                error => Result<Response>.Fail(error));
        }
    }

    internal sealed record RequestBody(string Token, string BaseUrl, string? ProviderType);

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/validate-token", static async (
                    RequestBody body,
                    IQueryHandler<Query, Response> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<BaseUrlVo> baseUrlResult = BaseUrlVo.Create(body.BaseUrl);
                    if (baseUrlResult is Result<BaseUrlVo>.Failure baseUrlFailure)
                    {
                        return (Results<Ok<Response>, BadRequest<string>>)TypedResults.BadRequest(
                            baseUrlFailure.Error.Message);
                    }

                    BaseUrlVo parsedBaseUrl = ((Result<BaseUrlVo>.Success)baseUrlResult).Value;

                    if (!ProviderTypes.IsKnown(body.ProviderType))
                    {
                        return TypedResults.BadRequest(
                            $"Provider type '{body.ProviderType}' is not supported. Only 'github' and 'gitlab' are supported.");
                    }

                    Uri apiBaseUrl = string.Equals(body.ProviderType, ProviderTypes.GitLab, StringComparison.OrdinalIgnoreCase)
                        ? GitLabCredential.DeriveApiBaseUrl(parsedBaseUrl)
                        : GitHubCredential.DeriveApiBaseUrl(parsedBaseUrl);

                    Result<Response> result = await handler.HandleAsync(
                        new Query(body.Token, apiBaseUrl, body.ProviderType!),
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
