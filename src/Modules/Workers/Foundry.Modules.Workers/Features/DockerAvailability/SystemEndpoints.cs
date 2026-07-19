using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Workers.Features.DockerAvailability;

internal static class SystemEndpoints
{
    internal static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder systemGroup = routes.MapGroup("/api/system")
            .WithTags("System");

        GetSystemStatus.Endpoint.Map(systemGroup);

        return routes;
    }
}
