using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features;

/// <summary>
/// Retries a <see cref="ContinuableFailedIssue"/> from scratch: deletes its work branch on the
/// provider, then requeues the issue fresh. The worker pre-creates its branch from the default
/// branch and treats an existing branch as reusable, so the branch must be gone before the next
/// dispatch — otherwise the "fresh" worker would pick up the previous commits.
/// </summary>
internal static class RetryIssueFresh
{
    internal sealed record Command(IssueId IssueId) : ICommand<IssueDetail>;

    internal sealed class Handler(
        DbContext db,
        IDomainEventDispatcher domainEventDispatcher,
        TimeProvider timeProvider,
        IIssueQueries issueQueries,
        IWorkBranchCommands workBranchCommands,
        ILogger<Handler> logger)
        : ICommandHandler<Command, IssueDetail>
    {
        public async Task<Result<IssueDetail>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            Issue? issue = await db.Set<Issue>()
                .FirstOrDefaultAsync(i => i.Id == command.IssueId, cancellationToken);

            if (issue is null)
            {
                return Result<IssueDetail>.Fail(IssueErrors.NotFound(command.IssueId));
            }

            if (issue is not ContinuableFailedIssue continuableFailed)
            {
                return Result<IssueDetail>.Fail(IssueErrors.WrongState(command.IssueId, "continuable_failed"));
            }

            // Delete before transitioning: if the delete fails the issue stays continuable-failed
            // (the operator can still Retry or try again), and a delete that succeeded before a
            // failed transition is harmless — the repeated delete reports the branch as absent.
            if (!string.IsNullOrWhiteSpace(continuableFailed.BranchName))
            {
                Result<bool> deleteResult = await workBranchCommands.DeleteBranchAsync(
                    continuableFailed.MonitoredRepositoryId,
                    continuableFailed.BranchName,
                    cancellationToken);

                if (deleteResult is Result<bool>.Failure deleteFailure)
                {
                    logger.LogWarning(
                        "Retry fresh for issue #{IssueNumber} aborted: deleting branch {BranchName} failed: {Error}",
                        continuableFailed.IssueNumber,
                        continuableFailed.BranchName,
                        deleteFailure.Error.Message);

                    return Result<IssueDetail>.Fail(IssueErrors.BranchDiscardFailed(
                        command.IssueId,
                        continuableFailed.BranchName,
                        deleteFailure.Error.Message));
                }
            }

            FreshQueuedIssue next = continuableFailed.RetryFresh(timeProvider.GetUtcNow());
            await db.TransitionAsync(issue, next, domainEventDispatcher, cancellationToken);

            logger.LogInformation(
                "Retry fresh: discarded branch {BranchName} and re-queued issue #{IssueNumber} fresh.",
                continuableFailed.BranchName,
                continuableFailed.IssueNumber);

            return await issueQueries.GetIssueDetailAsync(command.IssueId, cancellationToken);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/{id:guid}/retry-fresh", static async (
                    Guid id,
                    ICommandHandler<Command, IssueDetail> handler,
                    CancellationToken cancellationToken) =>
                {
                    IssueId issueId = IssueId.From(id);
                    Result<IssueDetail> result = await handler.HandleAsync(new Command(issueId), cancellationToken);

                    return result.Match<Results<Ok<IssueDetail>, ProblemHttpResult>>(
                        detail => TypedResults.Ok(detail),
                        error => error.Code switch
                        {
                            IssueErrors.NotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            IssueErrors.WrongStateCode => error.ToProblem(StatusCodes.Status409Conflict),
                            IssueErrors.BranchDiscardFailedCode => error.ToProblem(StatusCodes.Status502BadGateway),
                            _ => TypedResults.Problem(title: "An unexpected error occurred.", statusCode: StatusCodes.Status500InternalServerError),
                        });
                })
                .WithName("RetryIssueFresh")
                .WithSummary("Discards a continuable failure's branch and requeues the issue fresh")
                .Produces<IssueDetail>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status502BadGateway)
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }
    }
}
