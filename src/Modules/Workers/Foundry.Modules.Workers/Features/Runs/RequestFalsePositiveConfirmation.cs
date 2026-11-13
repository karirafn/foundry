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
/// Confirm false positives (#899): an operator confirms every secret finding of a publish-blocked run as a false
/// positive, and the run publishes again from its retained Hand-off volume with exactly those findings suppressed. The
/// request echoes the block time the operator saw, so a finding set is only ever confirmed by a client that rendered it.
/// </summary>
internal static class RequestFalsePositiveConfirmation
{
    /// <param name="PublishBlockedAt">The run's block time as the operator's client rendered it.</param>
    internal sealed record Command(WorkerRunId WorkerRunId, DateTimeOffset PublishBlockedAt) : ICommand<Response>;

    internal sealed record Response(WorkerRunId WorkerRunId);

    internal sealed class Handler(
        DbContext db,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IDomainEventDispatcher domainEventDispatcher,
        TimeProvider timeProvider) : ICommandHandler<Command, Response>
    {
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            WorkerRun? run = await db.Set<WorkerRun>()
                .FirstOrDefaultAsync(r => r.Id == command.WorkerRunId, cancellationToken);

            if (run is null)
            {
                return Result<Response>.Fail(WorkerRunErrors.NotFound(command.WorkerRunId));
            }

            if (run is not PublishBlockedRun blocked)
            {
                return Result<Response>.Fail(WorkerRunErrors.NotPublishBlocked(command.WorkerRunId));
            }

            if (blocked.PublishBlockedAt != command.PublishBlockedAt)
            {
                return Result<Response>.Fail(WorkerRunErrors.FindingsChanged(command.WorkerRunId));
            }

            PublishingRun publishing = blocked.ConfirmFalsePositives(timeProvider.GetUtcNow());

            // Enqueued before the transition so the outbox harvests them atomically with the state change.
            await integrationEventDispatcher.DispatchAsync(
                [
                    new WorkerRunFalsePositivesConfirmed(blocked.Id, blocked.IssueId.Value),
                    new WorkerRunPublishPhaseChanged(blocked.Id, blocked.IssueId.Value, WorkerRunPublishPhase.Publishing),
                ],
                cancellationToken);
            await db.TransitionAsync(blocked, publishing, domainEventDispatcher, cancellationToken);

            return new Response(publishing.Id);
        }
    }

    internal static class Endpoint
    {
        /// <param name="PublishBlockedAt">Required; nullable only so a missing field is a 400 with a clear message.</param>
        internal sealed record RequestBody(DateTimeOffset? PublishBlockedAt);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/{workerRunId:guid}/confirm-false-positives", static async (
                    Guid workerRunId,
                    RequestBody body,
                    ICommandHandler<Command, Response> handler,
                    CancellationToken cancellationToken) =>
                {
                    if (body.PublishBlockedAt is not { } publishBlockedAt)
                    {
                        return (Results<Accepted<Response>, ProblemHttpResult>)WorkerRunErrors.PublishBlockedAtRequired()
                            .ToProblem(StatusCodes.Status400BadRequest);
                    }

                    Result<Response> result = await handler.HandleAsync(
                        new Command(WorkerRunId.From(workerRunId), publishBlockedAt),
                        cancellationToken);

                    return result.Match<Results<Accepted<Response>, ProblemHttpResult>>(
                        response => TypedResults.Accepted((string?)null, response),
                        error => error.Code switch
                        {
                            WorkerRunErrors.NotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            WorkerRunErrors.NotPublishBlockedCode => error.ToProblem(StatusCodes.Status409Conflict),
                            WorkerRunErrors.FindingsChangedCode => error.ToProblem(StatusCodes.Status409Conflict),
                            _ => TypedResults.Problem(
                                title: "An unexpected error occurred.",
                                statusCode: StatusCodes.Status500InternalServerError),
                        });
                })
                .WithName("RequestWorkerRunFalsePositiveConfirmation")
                .WithSummary("Confirms a publish-blocked run's secret findings as false positives and publishes it")
                .Produces<Response>(StatusCodes.Status202Accepted)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }
    }
}
