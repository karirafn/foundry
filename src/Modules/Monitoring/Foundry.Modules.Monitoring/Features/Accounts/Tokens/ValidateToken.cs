using System.Diagnostics;
using System.Text.Json.Serialization;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Domain.Services;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

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
        internal const string WrongTokenKind = "wrongTokenKind";
        internal const string IdentityUnresolved = "identityUnresolved";
        internal const string ProviderMismatch = "providerMismatch";
    }

    internal sealed record Query(string Token, Uri ApiBaseUrl, string ProviderType) : IQuery<Response>;

    /// <param name="Expiry">The expiry the provider reported, kept for the account the token is set on.</param>
    /// <param name="TokenExpiry">The expiry as judged when the token was checked.</param>
    /// <param name="WrongKind">Why the token is refused when <paramref name="Kind"/> is <c>wrongTokenKind</c>, such as <c>github-classic</c>.</param>
    /// <param name="NotAllowed">The labels, or GitLab identifiers, of permissions a GitLab token holds but must not.</param>
    /// <param name="BroadReach">How a GitLab token reaches beyond selected projects: <c>all-projects</c>, <c>personal-projects</c> or <c>instance</c>.</param>
    internal sealed record Response(
        string Kind,
        string? AccountName,
        IReadOnlyList<string> MissingScopes,
        string? DetectedProvider,
        [property: JsonIgnore] Domain.ValueObjects.TokenExpiry Expiry,
        TokenExpirySummary TokenExpiry,
        string? WrongKind,
        IReadOnlyList<string> NotAllowed,
        IReadOnlyList<string> BroadReach);

    internal sealed class Handler(
        GitHubHttpClient gitHubHttpClient,
        GitLabHttpClient gitLabHttpClient,
        TimeProvider timeProvider)
        : IQueryHandler<Query, Response>
    {
        public async Task<Result<Response>> HandleAsync(Query query, CancellationToken cancellationToken)
        {
            if (string.Equals(query.ProviderType, ProviderTypes.GitLab, StringComparison.OrdinalIgnoreCase))
            {
                Result<TokenValidationOutcome> gitLabResult = await gitLabHttpClient.ValidateTokenAsync(
                    query.ApiBaseUrl, query.Token, cancellationToken);
                return gitLabResult.Match(
                    outcome => Result<Response>.Ok(MapOutcomeToResponse(outcome, timeProvider.GetUtcNow())),
                    error => Result<Response>.Fail(error));
            }

            Result<TokenValidationOutcome> gitHubResult = await gitHubHttpClient.ValidateTokenAsync(
                query.ApiBaseUrl, query.Token, cancellationToken);
            return gitHubResult.Match(
                outcome => Result<Response>.Ok(MapOutcomeToResponse(outcome, timeProvider.GetUtcNow())),
                error => Result<Response>.Fail(error));
        }

        private static Response MapOutcomeToResponse(TokenValidationOutcome outcome, DateTimeOffset now)
        {
            TokenExpiry expiry = outcome switch
            {
                TokenValidationOutcome.AuthenticatedOutcome auth => auth.TokenExpiry,
                TokenValidationOutcome.GrantReadOutcome grantRead => grantRead.TokenExpiry,
                TokenValidationOutcome.WrongTokenKindOutcome wrongKind => wrongKind.TokenExpiry,
                _ => new TokenExpiry.NotChecked(),
            };
            TokenExpirySummary summary = TokenExpirySummaries.From(expiry, now);

            return outcome switch
            {
                TokenValidationOutcome.AuthenticatedOutcome auth => Authenticated(
                    auth.AccountName, auth.MissingScopes, [], [], expiry, summary),
                TokenValidationOutcome.GrantReadOutcome grantRead => FromGrant(grantRead, expiry, summary),
                TokenValidationOutcome.AuthenticationFailedOutcome => Refused(Kinds.AuthenticationFailed, expiry, summary),
                TokenValidationOutcome.WrongTokenKindOutcome wrongKind => Refused(Kinds.WrongTokenKind, expiry, summary) with
                {
                    AccountName = wrongKind.AccountName,
                    WrongKind = wrongKind.WrongKind.WireName(),
                },
                TokenValidationOutcome.IdentityUnresolvedOutcome => Refused(Kinds.IdentityUnresolved, expiry, summary),
                TokenValidationOutcome.ProviderMismatchOutcome mismatch => Refused(Kinds.ProviderMismatch, expiry, summary) with
                {
                    DetectedProvider = mismatch.DetectedProvider,
                },
                _ => throw new UnreachableException($"Unhandled outcome: {outcome.GetType().Name}"),
            };
        }

        private static Response FromGrant(
            TokenValidationOutcome.GrantReadOutcome grantRead,
            TokenExpiry expiry,
            TokenExpirySummary summary)
        {
            GitLabGrantEvaluation evaluation = grantRead.Grant.Evaluate(TokenPermissions.GitLabGrantRequirements);
            return Authenticated(
                grantRead.AccountName,
                TokenPermissions.LabelsOf(ProviderTypes.GitLab, evaluation.Missing),
                TokenPermissions.GitLabLabelsOrIdentifiers(evaluation.NotAllowed),
                TokenPermissions.GitLabBroadReachWireNames(evaluation.BroadReach),
                expiry,
                summary);
        }

        private static Response Authenticated(
            string accountName,
            IReadOnlyList<string> missing,
            IReadOnlyList<string> notAllowed,
            IReadOnlyList<string> broadReach,
            TokenExpiry expiry,
            TokenExpirySummary summary) =>
            new(
                Kind: Kinds.Authenticated,
                AccountName: accountName,
                MissingScopes: missing,
                DetectedProvider: null,
                Expiry: expiry,
                TokenExpiry: summary,
                WrongKind: null,
                NotAllowed: notAllowed,
                BroadReach: broadReach);

        private static Response Refused(string kind, TokenExpiry expiry, TokenExpirySummary summary) =>
            new(
                Kind: kind,
                AccountName: null,
                MissingScopes: [],
                DetectedProvider: null,
                Expiry: expiry,
                TokenExpiry: summary,
                WrongKind: null,
                NotAllowed: [],
                BroadReach: []);
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
                        return (Results<Ok<Response>, ProblemHttpResult>)
                            baseUrlFailure.Error.ToProblem(StatusCodes.Status400BadRequest);
                    }

                    BaseUrlVo parsedBaseUrl = ((Result<BaseUrlVo>.Success)baseUrlResult).Value;

                    Result hostGuardResult = await providerHostGuard.EnsureAllowedAsync(parsedBaseUrl, cancellationToken);
                    if (hostGuardResult is Result.Failure hostGuardFailure)
                    {
                        return hostGuardFailure.Error.ToProblem(StatusCodes.Status400BadRequest);
                    }

                    if (!ProviderTypes.IsKnown(body.ProviderType))
                    {
                        return ProviderTypeErrors.UnknownProviderType(body.ProviderType)
                            .ToProblem(StatusCodes.Status400BadRequest);
                    }

                    Uri apiBaseUrl = string.Equals(body.ProviderType, ProviderTypes.GitLab, StringComparison.OrdinalIgnoreCase)
                        ? GitLabCredential.DeriveApiBaseUrl(parsedBaseUrl)
                        : GitHubCredential.DeriveApiBaseUrl(parsedBaseUrl);

                    Result<Response> result = await handler.HandleAsync(
                        new Query(body.Token, apiBaseUrl, body.ProviderType),
                        cancellationToken);

                    return result.Match<Results<Ok<Response>, ProblemHttpResult>>(
                        response => TypedResults.Ok(response),
                        error => error.ToProblem(StatusCodes.Status400BadRequest));
                })
                .WithName("ValidateToken")
                .WithSummary("Validates a personal access token for the given provider")
                .Produces<Response>()
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
