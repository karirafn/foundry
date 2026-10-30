using Foundry.Modules.Credentials.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// GET /api/credentials/usage
/// Returns the current in-memory usage snapshot, or 204 No Content when none exists.
/// </summary>
internal static class GetUsageSnapshot
{
    internal static class Endpoint
    {
        internal static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/usage", static (IUsageSnapshotState state) => Handle(state))
                .WithName("GetUsageSnapshot")
                .WithSummary("Returns the current Claude subscription usage snapshot, or 204 when no snapshot exists")
                .Produces<UsageSnapshotResponse>()
                .Produces(StatusCodes.Status204NoContent);
        }

        internal static IResult Handle(IUsageSnapshotState state)
        {
            UsageSnapshot? current = state.Current;

            if (current is null)
            {
                return TypedResults.NoContent();
            }

            return TypedResults.Ok(current.ToResponse());
        }
    }
}
