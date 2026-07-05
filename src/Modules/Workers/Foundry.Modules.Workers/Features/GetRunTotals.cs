using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Workers.Features;

internal static class GetRunTotals
{
    /// <summary>
    /// Query parameters for the run-totals endpoint.
    /// When <see cref="From"/> is omitted it defaults to <see cref="DateTimeOffset.MinValue"/> (all-time).
    /// When <see cref="To"/> is omitted it defaults to <see cref="DateTimeOffset.UtcNow"/> (up to now).
    /// </summary>
    internal sealed record Range(
        [FromQuery(Name = "from")] DateTimeOffset? From,
        [FromQuery(Name = "to")] DateTimeOffset? To);

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/", static async (
                    [AsParameters] Range range,
                    IWorkerRunQueries queries,
                    CancellationToken cancellationToken) =>
                {
                    DateTimeOffset from = range.From ?? DateTimeOffset.MinValue;
                    DateTimeOffset to = range.To ?? DateTimeOffset.UtcNow;

                    RunTotals totals = await queries.GetRunTotalsAsync(from, to, cancellationToken);

                    return TypedResults.Ok(totals);
                })
                .WithName("GetRunTotals")
                .WithSummary("Gets aggregated worker-run totals across all issues for a time window")
                .Produces<RunTotals>();
        }
    }
}
