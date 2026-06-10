using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Infrastructure;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Settings.Features;

internal static class ScanOAuthCredentials
{
    internal sealed record Query : IQuery<OAuthScanResponse>;

    internal sealed record OAuthScanResponse(
        bool AccessTokenPresent,
        bool RefreshTokenPresent,
        DateTimeOffset? ExpiresAt,
        string? SubscriptionType);

    internal sealed class Handler(IOAuthCredentialScanner scanner) : IQueryHandler<Query, OAuthScanResponse>
    {
        public async Task<Result<OAuthScanResponse>> HandleAsync(Query query, CancellationToken cancellationToken)
        {
            Result<OAuthCredentials> result = await scanner.ScanAsync(cancellationToken);

            if (result is Result<OAuthCredentials>.Failure failure)
            {
                return Result<OAuthScanResponse>.Fail(failure.Error);
            }

            if (result is not Result<OAuthCredentials>.Success success)
            {
                return Result<OAuthScanResponse>.Fail(SettingsErrors.OAuthCredentialsNotFound);
            }

            OAuthCredentials credentials = success.Value;
            return new OAuthScanResponse(
                AccessTokenPresent: credentials.AccessToken.Length > 0,
                RefreshTokenPresent: credentials.RefreshToken.Length > 0,
                ExpiresAt: credentials.ExpiresAt,
                SubscriptionType: credentials.SubscriptionType);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/oauth/scan", static async (
                    IQueryHandler<Query, OAuthScanResponse> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<OAuthScanResponse> result = await handler.HandleAsync(
                        new Query(),
                        cancellationToken);

                    return result.Match<Results<Ok<OAuthScanResponse>, BadRequest<string>>>(
                        response => TypedResults.Ok(response),
                        error => TypedResults.BadRequest(error.Message));
                })
                .WithName("ScanOAuthCredentials")
                .WithSummary("Scans for OAuth credentials in well-known file system locations")
                .Produces<OAuthScanResponse>()
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
