using Foundry.Modules.Workers.Domain;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features;

internal static class GetWorkerRunLog
{
    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/{workerRunId:guid}/log", static async (
                    Guid workerRunId,
                    DbContext db,
                    CancellationToken cancellationToken) =>
                {
                    WorkerRunId id = WorkerRunId.From(workerRunId);

                    WorkerRun? run = await db.Set<WorkerRun>()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

                    if (run is null)
                    {
                        return TypedResults.NotFound() as IResult;
                    }

                    if (run is not FailedRun { ContainerOutput.Length: > 0 } failedRun)
                    {
                        return TypedResults.NoContent();
                    }

                    return TypedResults.Text(failedRun.ContainerOutput, contentType: "text/plain");
                })
                .WithName("GetWorkerRunLog")
                .WithSummary("Gets persisted container log for a failed worker run")
                .Produces<string>(contentType: "text/plain")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
