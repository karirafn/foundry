using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Workers.Features;

internal static class GetWorkerRunDetail
{
    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/{workerRunId:guid}", static async (
                    Guid workerRunId,
                    IWorkerRunQueries queries,
                    CancellationToken cancellationToken) =>
                {
                    Result<WorkerRunDetail> result = await queries.GetWorkerRunDetailAsync(
                        workerRunId,
                        cancellationToken);

                    return result.Match<Results<Ok<WorkerRunDetail>, NotFound>>(
                        detail => TypedResults.Ok(detail),
                        _ => TypedResults.NotFound());
                })
                .WithName("GetWorkerRunDetail")
                .WithSummary("Gets worker run detail by ID")
                .Produces<WorkerRunDetail>()
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
