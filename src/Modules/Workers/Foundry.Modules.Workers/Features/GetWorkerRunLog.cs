using System.Diagnostics;

using Foundry.Modules.Workers.Contracts.Queries;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Workers.Features;

internal static class GetWorkerRunLog
{
    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/{workerRunId:guid}/log", static async (
                    Guid workerRunId,
                    IWorkerRunQueries queries,
                    CancellationToken cancellationToken) =>
                {
                    WorkerRunLogResult logResult = await queries.GetWorkerRunLogAsync(
                        workerRunId,
                        cancellationToken);

                    return logResult switch
                    {
                        WorkerRunLogResult.RunNotFound =>
                            (Results<ContentHttpResult, NoContent, NotFound>)TypedResults.NotFound(),
                        WorkerRunLogResult.NoLog =>
                            TypedResults.NoContent(),
                        WorkerRunLogResult.LogAvailable log =>
                            TypedResults.Text(log.LogText, contentType: "text/plain"),
                        _ => throw new UnreachableException(
                            $"Unhandled {nameof(WorkerRunLogResult)} subtype: {logResult.GetType().Name}"),
                    };
                })
                .WithName("GetWorkerRunLog")
                .WithSummary("Gets persisted container log for a failed worker run")
                .Produces<string>(contentType: "text/plain")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
