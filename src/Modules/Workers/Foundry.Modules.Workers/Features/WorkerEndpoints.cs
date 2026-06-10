using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Workers.Features;

internal static class WorkerEndpoints
{
    internal static IEndpointRouteBuilder MapWorkerEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/workers")
            .WithTags("Workers");

        GetReports.Endpoint.Map(group);

        return routes;
    }
}
