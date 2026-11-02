using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.Dispatch;

internal static class GetDispatchCapacity
{
    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/", static async (
                    DbContext dbContext,
                    IGlobalSettingsQueries settingsQueries,
                    CancellationToken cancellationToken) =>
                {
                    // Same occupancy count the dispatch gate uses, so the dashboard never recomputes it.
                    int occupiedSlots = await dbContext.GetSlotOccupancyCountAsync(cancellationToken);
                    int maxConcurrent = await settingsQueries.GetMaxConcurrentAsync(cancellationToken);
                    int freeSlots = Math.Max(0, maxConcurrent - occupiedSlots);

                    return TypedResults.Ok(new DispatchCapacity(maxConcurrent, occupiedSlots, freeSlots));
                })
                .WithName("GetDispatchCapacity")
                .WithSummary("Gets the configured dispatch slot limit and the slots currently occupied and free")
                .Produces<DispatchCapacity>();
        }
    }
}
