using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Monitoring.Features.RateBudget;

internal static class RateBudgetEndpoints
{
    internal static IEndpointRouteBuilder MapRateBudgetEndpoints(this IEndpointRouteBuilder routes)
    {
        // /api/rate-budget intentionally serves public, unauthenticated budget headroom —
        // the dashboard must show rate-limit status before a session is established.
        // Revisit when an auth layer is introduced.
        RouteGroupBuilder group = routes.MapGroup("/api/rate-budget")
            .WithTags("RateBudget");

        GetRateBudget.Endpoint.Map(group);

        return routes;
    }
}
