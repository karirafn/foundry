using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features.AwaitingInput;

/// <summary>
/// Retry post (ADR 0096): an operator sends an awaiting-input issue's failed question post back to pending, and the
/// awaiting-input step is nudged to post it straight away. Any other issue, or a post that has not failed, is left as
/// it is.
/// </summary>
internal static class RetryQuestionPost
{
    internal sealed record Command(IssueId IssueId) : ICommand<IssueDetail>;

    internal sealed class Handler(
        DbContext db,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IIssueQueries issueQueries)
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

            if (issue is not AwaitingInputIssue awaiting || !awaiting.RetryQuestionPost())
            {
                return Result<IssueDetail>.Fail(IssueErrors.WrongState(command.IssueId, "awaiting_input with a failed question post"));
            }

            // The nudge that posts a newly entered wait's questions posts the retried ones the same way.
            await integrationEventDispatcher.DispatchAsync(
                [new IssueEnteredAwaitingInput(awaiting.MonitoredRepositoryId, awaiting.IssueNumber)],
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            return await issueQueries.GetIssueDetailAsync(command.IssueId, cancellationToken);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/{id:guid}/retry-post", static async (
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
                            _ => TypedResults.Problem(title: "An unexpected error occurred.", statusCode: StatusCodes.Status500InternalServerError),
                        });
                })
                .WithName("RetryQuestionPost")
                .WithSummary("Sends an awaiting-input issue's failed question post back to pending so it is posted again")
                .Produces<IssueDetail>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }
    }
}
