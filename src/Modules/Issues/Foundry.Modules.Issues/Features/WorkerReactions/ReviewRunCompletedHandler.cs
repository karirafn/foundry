using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.WorkerReactions;

/// <summary>
/// Moves a reviewed issue on by its review's outcome (ADR 0113): an approval merges when the checks passed and the
/// repository merges on green, and everything else waits for a person's review. Replay-safe: an issue no longer
/// reviewing with this run (a redelivery, or the issue moved on) is left as it is.
/// </summary>
[IntegrationEventHandlerIdentity("Issues.ReviewRunCompleted")]
internal sealed class ReviewRunCompletedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    TimeProvider timeProvider,
    ILogger<ReviewRunCompletedHandler> logger) : IIntegrationEventHandler<ReviewRunCompleted>
{
    public async Task HandleAsync(ReviewRunCompleted @event, CancellationToken cancellationToken)
    {
        IssueId issueId = IssueId.From(@event.IssueId);

        Issue? issue = await db.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

        if (issue is not ReviewInProgressIssue reviewing || reviewing.WorkerRunId != @event.WorkerRunId)
        {
            logger.LogInformation(
                "ReviewRunCompleted for issue {IssueId} (run {WorkerRunId}) found it no longer in review with that run (state: {State}); ignoring.",
                @event.IssueId,
                @event.WorkerRunId,
                issue?.GetType().Name ?? "not found");
            return;
        }

        Issue next = reviewing.ResolveVerdict(@event.Outcome, timeProvider.GetUtcNow());
        await db.TransitionAsync(reviewing, next, domainEventDispatcher, cancellationToken);
    }
}
