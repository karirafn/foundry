using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.Runs;

/// <summary>
/// Records a user's request to cancel a running worker. The handler only stamps the request on the
/// <see cref="ActiveRun"/>; <c>WorkerDispatchService</c> stays the single writer of run state and
/// stops the container on its next tick.
/// </summary>
internal static class RequestRunCancellation
{
    internal sealed record Command(WorkerRunId WorkerRunId) : ICommand<Response>;

    internal sealed record Response(WorkerRunId WorkerRunId, DateTimeOffset CancelRequestedAt);

    internal sealed class Handler(DbContext db, TimeProvider timeProvider) : ICommandHandler<Command, Response>
    {
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            WorkerRun? run = await db.Set<WorkerRun>()
                .FirstOrDefaultAsync(r => r.Id == command.WorkerRunId, cancellationToken);

            if (run is null)
            {
                return Result<Response>.Fail(WorkerRunErrors.NotFound(command.WorkerRunId));
            }

            if (run is not ActiveRun active)
            {
                return Result<Response>.Fail(WorkerRunErrors.NotCancellable(command.WorkerRunId));
            }

            // Idempotent: a repeated request keeps the first timestamp and saves nothing.
            active.RequestCancel(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);

            return new Response(active.Id, active.CancelRequestedAt!.Value);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/{workerRunId:guid}/cancellation", static async (
                    Guid workerRunId,
                    ICommandHandler<Command, Response> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<Response> result = await handler.HandleAsync(
                        new Command(WorkerRunId.From(workerRunId)),
                        cancellationToken);

                    return result.Match<Results<Accepted<Response>, ProblemHttpResult>>(
                        response => TypedResults.Accepted((string?)null, response),
                        error => error.Code switch
                        {
                            WorkerRunErrors.NotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            WorkerRunErrors.NotCancellableCode => error.ToProblem(StatusCodes.Status409Conflict),
                            _ => TypedResults.Problem(
                                title: "An unexpected error occurred.",
                                statusCode: StatusCodes.Status500InternalServerError),
                        });
                })
                .WithName("RequestWorkerRunCancellation")
                .WithSummary("Requests cancellation of a running worker; the dispatch poll stops it")
                .Produces<Response>(StatusCodes.Status202Accepted)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }
    }
}
