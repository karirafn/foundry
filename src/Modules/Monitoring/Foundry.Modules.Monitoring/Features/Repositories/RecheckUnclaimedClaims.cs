using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class RecheckUnclaimedClaims
{
    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("unclaimed/recheck", static async (
                    string? host,
                    ClaimRecheck claimRecheck,
                    CancellationToken cancellationToken) =>
                {
                    if (string.IsNullOrWhiteSpace(host))
                    {
                        return (Results<Ok<RecheckClaimsResponse>, ProblemHttpResult>)RepositoryErrors.HostRequired()
                            .ToProblem(StatusCodes.Status400BadRequest);
                    }

                    int reclaimed = await claimRecheck.RecheckHostAsync(host, cancellationToken);

                    return TypedResults.Ok(new RecheckClaimsResponse(reclaimed));
                })
                .WithName("RecheckUnclaimedClaims")
                .WithSummary("Re-derives the namespace claims of every account on a host with an unclaimed repository")
                .Produces<RecheckClaimsResponse>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
