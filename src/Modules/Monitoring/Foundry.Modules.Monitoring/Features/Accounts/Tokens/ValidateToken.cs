using System.Diagnostics;
using System.Text.Json.Serialization;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Services;
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
    internal static class Kinds
    {
        internal const string Authenticated = "authenticated";
        internal const string AuthenticationFailed = "authenticationFailed";
        internal const string ScopesUnverifiable = "scopesUnverifiable";
        internal const string IdentityUnresolved = "identityUnresolved";
        internal const string ProviderMismatch = "providerMismatch";
    }

    internal sealed record Query(string Token, Uri ApiBaseUrl, string ProviderType) : IQuery<Response>;

    internal sealed record Response(
        string Kind,
        string? AccountName,
        IReadOnlyList<string> MissingScopes,
        string? DetectedProvider);

    internal sealed class Handler(
        GitHubHttpClient gitHubHttpClient,
        GitLabHttpClient gitLabHttpClient)
        : IQueryHandler<Query, Response>
    {
        public async Task<Result<Response>> HandleAsync(Query query, CancellationToken cancellationToken)
        {
            if (string.Equals(query.ProviderType, ProviderTypes.GitLab, StringComparison.OrdinalIgnoreCase))
            {
                Result<TokenValidationOutcome> gitLabResult = await gitLabHttpClient.ValidateTokenAsync(
                    query.ApiBaseUrl, query.Token, cancellationToken);
                return gitLabResult.Match(
                    outcome => Result<Response>.Ok(MapOutcomeToResponse(outcome)),
                    error => Result<Response>.Fail(error));
            }

            Result<TokenValidationOutcome> gitHubResult = await gitHubHttpClient.ValidateTokenAsync(
                query.ApiBaseUrl, query.Token, cancellationToken);
            return gitHubResult.Match(
                outcome => Result<Response>.Ok(MapOutcomeToResponse(outcome)),
                error => Result<Response>.Fail(error));
        }

        private static Response MapOutcomeToResponse(TokenValidationOutcome outcome) =>
            outcome switch
            {
                TokenValidationOutcome.AuthenticatedOutcome auth => new Response(
                    Kind: Kinds.Authenticated,
                    AccountName: auth.AccountName,
                    MissingScopes: auth.MissingScopes,
                    DetectedProvider: null),
                TokenValidationOutcome.AuthenticationFailedOutcome => new Response(
                    Kind: Kinds.AuthenticationFailed,
                    AccountName: null,
                    MissingScopes: [],
                    DetectedProvider: null),
                TokenValidationOutcome.ScopesUnverifiableOutcome unverifiable => new Response(
                    Kind: Kinds.ScopesUnverifiable,
                    AccountName: unverifiable.AccountName,
                    MissingScopes: [],
                    DetectedProvider: null),
                TokenValidationOutcome.IdentityUnresolvedOutcome => new Response(
                    Kind: Kinds.IdentityUnresolved,
                    AccountName: null,
                    MissingScopes: [],
                    DetectedProvider: null),
                TokenValidationOutcome.ProviderMismatchOutcome mismatch => new Response(
                    Kind: Kinds.ProviderMismatch,
                    AccountName: null,
                    MissingScopes: [],
                    DetectedProvider: mismatch.DetectedProvider),
                _ => throw new UnreachableException($"Unhandled outcome: {outcome.GetType().Name}"),
            };
    }

    internal sealed record RequestBody(string Token, string BaseUrl, [property: JsonRequired] string ProviderType);

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/validate-token", static async (
                    RequestBody body,
                    IQueryHandler<Query, Response> handler,
                    ProviderHostGuard providerHostGuard,
                    CancellationToken cancellationToken) =>
                {
                    Result<BaseUrlVo> baseUrlResult = BaseUrlVo.Create(body.BaseUrl);
                    if (baseUrlResult is Result<BaseUrlVo>.Failure baseUrlFailure)
                    {
                        return (Results<Ok<Response>, BadRequest<string>>)TypedResults.BadRequest(
                            baseUrlFailure.Error.Message);
                    }

                    BaseUrlVo parsedBaseUrl = ((Result<BaseUrlVo>.Success)baseUrlResult).Value;

                    Result hostGuardResult = await providerHostGuard.EnsureAllowedAsync(parsedBaseUrl, cancellationToken);
                    if (hostGuardResult is Result.Failure hostGuardFailure)
                    {
                        return TypedResults.BadRequest(hostGuardFailure.Error.Message);
                    }

                    if (!ProviderTypes.IsKnown(body.ProviderType))
                    {
                        return TypedResults.BadRequest(
                            $"Provider type '{body.ProviderType}' is not supported. Only 'github' and 'gitlab' are supported.");
                    }

                    Uri apiBaseUrl = string.Equals(body.ProviderType, ProviderTypes.GitLab, StringComparison.OrdinalIgnoreCase)
                        ? GitLabCredential.DeriveApiBaseUrl(parsedBaseUrl)
                        : GitHubCredential.DeriveApiBaseUrl(parsedBaseUrl);

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
