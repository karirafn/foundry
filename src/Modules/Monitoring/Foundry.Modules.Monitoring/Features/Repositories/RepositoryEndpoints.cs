using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class RepositoryEndpoints
{
    internal static IEndpointRouteBuilder MapRepositoryEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder accountScopedGroup = routes.MapGroup("/api/accounts/{accountId:guid}/repositories")
            .WithTags("Repositories");

        GetRepositories.Endpoint.Map(accountScopedGroup);
        GetAvailableRepositories.Endpoint.Map(accountScopedGroup);
        CreateRepository.Endpoint.Map(accountScopedGroup);
        UpdateRepository.Endpoint.Map(accountScopedGroup);
        DeleteRepository.Endpoint.Map(accountScopedGroup);
        RecheckRepositoryEligibility.Endpoint.Map(accountScopedGroup);

        RouteGroupBuilder repositoriesGroup = routes.MapGroup("/api/repositories")
            .WithTags("Repositories");

        MoveRepository.Endpoint.Map(repositoriesGroup);

        return routes;
    }
}
