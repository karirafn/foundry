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
    internal sealed record Query : IQuery<OAuthCredentials>;

    internal sealed class Handler(IOAuthCredentialScanner scanner) : IQueryHandler<Query, OAuthCredentials>
    {
        public Task<Result<OAuthCredentials>> HandleAsync(Query query, CancellationToken cancellationToken) =>
            scanner.ScanAsync(cancellationToken);
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/oauth/scan", static async (
                    IQueryHandler<Query, OAuthCredentials> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<OAuthCredentials> result = await handler.HandleAsync(
                        new Query(),
                        cancellationToken);

                    return result.Match<Results<Ok<OAuthCredentials>, BadRequest<string>>>(
                        credentials => TypedResults.Ok(credentials),
                        error => TypedResults.BadRequest(error.Message));
                })
                .WithName("ScanOAuthCredentials")
                .WithSummary("Scans for OAuth credentials in well-known file system locations")
                .Produces<OAuthCredentials>()
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
