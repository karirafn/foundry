using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// POST /api/credentials/usage/refresh
/// Requests an on-demand usage poll through the single-flight coordinator without waiting for it.
/// Returns 202 Accepted when a poll was started or is already in flight, 200 OK with
/// <c>status: "current"</c> when the last successful snapshot is younger than the refresh floor,
/// and 409 Conflict when the account cannot report usage or a login is active. The body never
/// carries a snapshot — the poll's result reaches clients via the <c>UsageSnapshotUpdated</c> push.
/// </summary>
internal static class RefreshUsage
{
    internal static class Statuses
    {
        internal const string Started = "started";
        internal const string InFlight = "inFlight";
        internal const string Current = "current";
        internal const string Unavailable = "unavailable";
        internal const string LoginActive = "loginActive";
    }

    internal sealed record Response(string Status);

    internal static class Endpoint
    {
        internal static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/usage/refresh", static async (
                    IUsagePollCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                await HandleAsync(coordinator, cancellationToken))
                .WithName("RefreshUsage")
                .WithSummary("Requests an on-demand Claude usage poll; the result arrives via the UsageSnapshotUpdated push")
                .Produces<Response>(StatusCodes.Status200OK)
                .Produces<Response>(StatusCodes.Status202Accepted)
                .Produces<Response>(StatusCodes.Status409Conflict);
        }

        internal static async Task<IResult> HandleAsync(
            IUsagePollCoordinator coordinator,
            CancellationToken cancellationToken)
        {
            UsageRefreshOutcome outcome = await coordinator.RequestRefreshAsync(cancellationToken);

            return outcome switch
            {
                UsageRefreshOutcome.Started =>
                    TypedResults.Accepted((string?)null, new Response(Statuses.Started)),
                UsageRefreshOutcome.InFlight =>
                    TypedResults.Accepted((string?)null, new Response(Statuses.InFlight)),
                UsageRefreshOutcome.AlreadyCurrent =>
                    TypedResults.Ok(new Response(Statuses.Current)),
                UsageRefreshOutcome.Unavailable =>
                    TypedResults.Conflict(new Response(Statuses.Unavailable)),
                UsageRefreshOutcome.LoginActive =>
                    TypedResults.Conflict(new Response(Statuses.LoginActive)),
                _ => throw new System.Diagnostics.UnreachableException(
                    $"Unexpected UsageRefreshOutcome: {outcome}"),
            };
        }
    }
}
