using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features;

internal static class RetryIssue
{
    internal sealed record Command(IssueId IssueId) : ICommand<IssueDetail>;

    internal sealed class Handler(
        DbContext db,
        IDomainEventDispatcher domainEventDispatcher,
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

            Issue next = issue switch
            {
                FailedIssue failed => failed.Retry(),
                ContinuableFailedIssue continuableFailed => continuableFailed.Retry(),
                RevisionFailedIssue revisionFailed => revisionFailed.Retry(),
                _ => issue,
            };

            if (ReferenceEquals(next, issue))
            {
                return Result<IssueDetail>.Fail(IssueErrors.WrongState(command.IssueId, "failed, continuable_failed, or revision_failed"));
            }

            await db.TransitionAsync(issue, next, domainEventDispatcher, cancellationToken);

            return await issueQueries.GetIssueDetailAsync(command.IssueId, cancellationToken);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/{id:guid}/retry", static async (
                    Guid id,
                    ICommandHandler<Command, IssueDetail> handler,
                    CancellationToken cancellationToken) =>
                {
                    IssueId issueId = IssueId.From(id);
                    Result<IssueDetail> result = await handler.HandleAsync(new Command(issueId), cancellationToken);

                    return result.Match<Results<Ok<IssueDetail>, NotFound, Conflict>>(
                        detail => TypedResults.Ok(detail),
                        error =>
                        {
                            if (error.Code == IssueErrors.NotFoundCode)
                            {
                                return TypedResults.NotFound();
                            }

                            return TypedResults.Conflict();
                        });
                })
                .WithName("RetryIssue")
                .WithSummary("Retries a failed issue")
                .Produces<IssueDetail>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict);
        }
    }
}
