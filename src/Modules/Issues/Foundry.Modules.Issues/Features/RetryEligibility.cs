using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features;

internal static class RetryEligibility
{
    internal sealed class Handler(
        DbContext db,
        IBranchProtectionValidator branchProtectionValidator,
        IDomainEventDispatcher dispatcher,
        IIssueQueries queries)
    {
        public async Task<Result<IssueDetail>> HandleAsync(IssueId issueId, CancellationToken cancellationToken)
        {
            Issue? issue = await db.Set<Issue>()
                .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

            if (issue is null)
            {
                return Result<IssueDetail>.Fail(IssueErrors.NotFound(issueId));
            }

            if (issue is not IneligibleIssue ineligible)
            {
                return Result<IssueDetail>.Fail(IssueErrors.WrongState(issueId, "ineligible"));
            }

            Result<IReadOnlyList<EligibilityViolationInfo>> validationResult =
                await branchProtectionValidator.ValidateAsync(ineligible.MonitoredRepositoryId, cancellationToken);

            List<EligibilityViolation> violations = validationResult switch
            {
                Result<IReadOnlyList<EligibilityViolationInfo>>.Success success when success.Value.Count > 0 =>
                    success.Value
                        .Select(v => EligibilityViolation.From(v.Rule, v.Description))
                        .ToList(),
                Result<IReadOnlyList<EligibilityViolationInfo>>.Failure =>
                    [EligibilityViolation.Unreachable()],
                _ => [],
            };

            if (violations.Count == 0)
            {
                Issue next = ineligible.ClearViolations();
                switch (next)
                {
                    case QueuedIssue queued:
                        await db.TransitionAsync(ineligible, queued, cancellationToken);
                        break;
                    case BlockedIssue blocked:
                        await db.TransitionAsync(ineligible, blocked, cancellationToken);
                        break;
                }

                await dispatcher.DispatchAsync(ineligible.DomainEvents, cancellationToken);
            }
            else
            {
                Result updateResult = ineligible.UpdateViolations(violations);
                if (updateResult.IsSuccess)
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            return await queries.GetIssueDetailAsync(issueId, cancellationToken);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/{id:guid}/retry-eligibility", static async (
                    Guid id,
                    Handler handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<IssueDetail> result = await handler.HandleAsync(
                        IssueId.From(id),
                        cancellationToken);

                    return result.Match(
                        detail => TypedResults.Ok(detail) as IResult,
                        error => error.Code == IssueErrors.NotFoundCode
                            ? TypedResults.NotFound()
                            : TypedResults.Conflict());
                })
                .WithName("RetryEligibility")
                .WithSummary("Retries the eligibility check for an ineligible issue")
                .Produces<IssueDetail>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict);
        }
    }
}
