using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Monitoring.Features.RateBudget;

internal static class RateBudgetEndpoints
{
    internal static IEndpointRouteBuilder MapRateBudgetEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/rate-budget")
            .WithTags("RateBudget");

        GetRateBudget.Endpoint.Map(group);

        return routes;
    }
}
