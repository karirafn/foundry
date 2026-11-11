using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
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
/// Retry publish (ADR 0100): an operator reruns Publish for a run whose publish retries were exhausted, from its
/// retained Hand-off volume. The run returns to publishing, where the publishing loop picks it up.
/// </summary>
internal static class RequestPublishRetry
{
    internal sealed record Command(WorkerRunId WorkerRunId) : ICommand<Response>;

    internal sealed record Response(WorkerRunId WorkerRunId);

    internal sealed class Handler(
        DbContext db,
        IIntegrationEventDispatcher integrationEventDispatcher,
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

            if (run is not PublishFailedRun { IsExhausted: true } failed)
            {
                return Result<Response>.Fail(WorkerRunErrors.NotAwaitingPublishRetry(command.WorkerRunId));
            }

            PublishingRun publishing = failed.RequestRetry();

            // Enqueued before the transition so the outbox harvests them atomically with the state change.
            await integrationEventDispatcher.DispatchAsync(
                [
                    new WorkerRunPublishRetryRequested(failed.Id, failed.IssueId.Value),
                    new WorkerRunPublishPhaseChanged(failed.Id, failed.IssueId.Value, WorkerRunPublishPhase.Publishing),
                ],
                cancellationToken);
            await db.TransitionAsync(failed, publishing, domainEventDispatcher, cancellationToken);

            return new Response(publishing.Id);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/{workerRunId:guid}/publish-retry", static async (
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
                            WorkerRunErrors.NotAwaitingPublishRetryCode => error.ToProblem(StatusCodes.Status409Conflict),
                            _ => TypedResults.Problem(
                                title: "An unexpected error occurred.",
                                statusCode: StatusCodes.Status500InternalServerError),
                        });
                })
                .WithName("RequestWorkerRunPublishRetry")
                .WithSummary("Reruns Publish for a run whose publish retries were exhausted")
                .Produces<Response>(StatusCodes.Status202Accepted)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }
    }
}
