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
/// An operator confirmed a publish-blocked run's secret findings as false positives (#899): the same run publishes again,
/// so its issue returns to work in progress and later completes, fails or is blocked again through the run's own events.
/// </summary>
[IntegrationEventHandlerIdentity("Issues.WorkerRunFalsePositivesConfirmed")]
internal sealed class WorkerRunFalsePositivesConfirmedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    TimeProvider timeProvider,
    ILogger<WorkerRunFalsePositivesConfirmedHandler> logger) : IIntegrationEventHandler<WorkerRunFalsePositivesConfirmed>
{
    public async Task HandleAsync(WorkerRunFalsePositivesConfirmed @event, CancellationToken cancellationToken)
    {
        IssueId issueId = IssueId.From(@event.IssueId);

        Issue? issue = await db.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

        if (issue is not PublishBlockedIssue blocked || blocked.WorkerRunId != @event.WorkerRunId)
        {
            logger.LogWarning(
                "WorkerRunFalsePositivesConfirmed for issue {IssueId} run {EventRunId} does not match an issue whose publish that run blocked (state: {State}); ignoring.",
                @event.IssueId,
                @event.WorkerRunId,
                issue?.GetType().Name ?? "not found");
            return;
        }

        Issue next = blocked.ConfirmFalsePositives(timeProvider.GetUtcNow());
        await db.TransitionAsync(blocked, next, domainEventDispatcher, cancellationToken);
    }
}
