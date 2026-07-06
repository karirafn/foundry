using Foundry.Modules.Credentials.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Credentials.Features;

internal static class CredentialsEndpoints
{
    internal static IEndpointRouteBuilder MapCredentialEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/credentials")
            .WithTags("Credentials");

        GetCredentials.Endpoint.Map(group);
        UpdateAuthMode.Endpoint.Map(group);

        return routes;
    }
}
