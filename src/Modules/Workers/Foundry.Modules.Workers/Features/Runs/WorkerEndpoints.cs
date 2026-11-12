using Foundry.Modules.Workers.Features.Dispatch;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Workers.Features.Runs;

internal static class WorkerEndpoints
{
    internal static IEndpointRouteBuilder MapWorkerEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder runsGroup = routes.MapGroup("/api/workers/runs")
            .WithTags("Workers");

        GetWorkerRunDetail.Endpoint.Map(runsGroup);
        GetWorkerRunLog.Endpoint.Map(runsGroup);
        RequestRunCancellation.Endpoint.Map(runsGroup);
        RequestPublishRetry.Endpoint.Map(runsGroup);
        RequestRunDiscard.Endpoint.Map(runsGroup);

        RouteGroupBuilder runTotalsGroup = routes.MapGroup("/api/workers/run-totals")
            .WithTags("Workers");

        GetRunTotals.Endpoint.Map(runTotalsGroup);

        RouteGroupBuilder capacityGroup = routes.MapGroup("/api/workers/capacity")
            .WithTags("Workers");

        GetDispatchCapacity.Endpoint.Map(capacityGroup);

        return routes;
    }
}
