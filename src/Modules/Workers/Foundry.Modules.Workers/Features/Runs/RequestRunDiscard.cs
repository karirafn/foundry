using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Publishing;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.Runs;

/// <summary>
/// Discard run (#788): an operator ends a run whose unpublished work waits for them — its publish was blocked by the
/// Diff gate, or its publish retries ran out — before the Hand-off volume's time to live does. The run's commits are
/// deleted with the volume and nothing is pushed; its issue fails, so Retry Issue starts a new run from the branch.
/// </summary>
internal static class RequestRunDiscard
{
    internal sealed record Command(WorkerRunId WorkerRunId) : ICommand<Response>;

    internal sealed record Response(WorkerRunId WorkerRunId);

    internal sealed class Handler(
        DbContext db,
        IWorkerOrchestrator orchestrator,
        IDomainEventDispatcher domainEventDispatcher) : ICommandHandler<Command, Response>
    {
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            WorkerRun? run = await db.Set<WorkerRun>()
                .FirstOrDefaultAsync(r => r.Id == command.WorkerRunId, cancellationToken);

            if (run is null)
            {
                return Result<Response>.Fail(WorkerRunErrors.NotFound(command.WorkerRunId));
            }

            UnpublishedRun? discardable = run switch
            {
                PublishBlockedRun blocked => blocked,
                PublishFailedRun { IsExhausted: true } exhausted => exhausted,
                _ => null,
            };

            if (discardable is null)
            {
                return Result<Response>.Fail(WorkerRunErrors.NotHoldingUnpublishedWork(command.WorkerRunId));
            }

            FailedRun discarded = ((IDiscardableRun)discardable).DiscardOnRequest();

            await RunDiscarder.DiscardAsync(
                db,
                orchestrator,
                domainEventDispatcher,
                discardable,
                discarded,
                cancellationToken);

            return new Response(discarded.Id);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/{workerRunId:guid}/discard", static async (
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
                            WorkerRunErrors.NotHoldingUnpublishedWorkCode => error.ToProblem(StatusCodes.Status409Conflict),
                            _ => TypedResults.Problem(
                                title: "An unexpected error occurred.",
                                statusCode: StatusCodes.Status500InternalServerError),
                        });
                })
                .WithName("RequestWorkerRunDiscard")
                .WithSummary("Discards a run holding unpublished work: its commits are deleted and nothing is pushed")
                .Produces<Response>(StatusCodes.Status202Accepted)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }
    }
}
