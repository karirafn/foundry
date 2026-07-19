using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Workers.Features.DockerAvailability;

internal sealed record SystemStatus(bool DockerAvailable);

internal static class GetSystemStatus
{
    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/status", static (IDockerAvailabilityState state) =>
                    TypedResults.Ok(new SystemStatus(state.IsAvailable)))
                .WithName("GetSystemStatus")
                .WithSummary("Returns system availability state, including whether the Docker daemon is reachable")
                .Produces<SystemStatus>()
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }
    }
}
