using Foundry.Modules.Monitoring.Features.Accounts.Tokens;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class ProviderEndpoints
{
    internal static IEndpointRouteBuilder MapProviderEndpoints(this IEndpointRouteBuilder routes)
    {
        // /api/providers intentionally serves public, unauthenticated provider metadata —
        // clients need token-requirements before they have a token to authenticate with.
        RouteGroupBuilder group = routes.MapGroup("/api/providers")
            .WithTags("Providers");

        GetTokenRequirements.Endpoint.Map(group);

        return routes;
    }
}
