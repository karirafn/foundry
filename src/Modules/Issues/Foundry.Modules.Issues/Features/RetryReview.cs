using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features;

/// <summary>Queues a review that failed open for review again, for the same reviewed commit (ADR 0113).</summary>
internal static class RetryReview
{
    internal sealed record Command(IssueId IssueId) : ICommand<IssueDetail>;

    internal sealed class Handler(
        DbContext db,
        IDomainEventDispatcher domainEventDispatcher,
        TimeProvider timeProvider,
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

            Issue? next = issue switch
            {
                ReviewIssue review => review.RetryReview(timeProvider.GetUtcNow()),
                _ => null,
            };

            if (next is null)
            {
                return Result<IssueDetail>.Fail(IssueErrors.WrongState(command.IssueId, "review"));
            }

            await db.TransitionAsync(issue, next, domainEventDispatcher, cancellationToken);

            return await issueQueries.GetIssueDetailAsync(command.IssueId, cancellationToken);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/{id:guid}/retry-review", static async (
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
                .WithName("RetryReview")
                .WithSummary("Queues a skipped review for review again, for the same reviewed commit")
                .Produces<IssueDetail>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }
    }
}
