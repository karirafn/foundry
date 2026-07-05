using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Credentials.Features.Login;

internal static class LoginEndpoints
{
    internal static IEndpointRouteBuilder MapLoginEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/settings/oauth/login")
            .WithTags("OAuth");

        StartLogin.Endpoint.Map(group);
        SubmitLoginCode.Endpoint.Map(group);

        return routes;
    }
}
