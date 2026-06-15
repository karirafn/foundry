using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class RepositoryEndpoints
{
    internal static IEndpointRouteBuilder MapRepositoryEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/accounts/{accountId:guid}/repositories")
            .WithTags("Repositories");

        GetRepositories.Endpoint.Map(group);
        GetAvailableRepositories.Endpoint.Map(group);
        CreateRepository.Endpoint.Map(group);
        UpdateRepository.Endpoint.Map(group);
        DeleteRepository.Endpoint.Map(group);

        return routes;
    }
}
