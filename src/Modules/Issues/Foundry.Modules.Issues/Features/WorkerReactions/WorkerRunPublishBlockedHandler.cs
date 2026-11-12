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
/// The Diff gate blocked the run's publish because it found possible secrets (#788): the issue moves to Needs attention,
/// where the only way on is to discard the run.
/// </summary>
[IntegrationEventHandlerIdentity("Issues.WorkerRunPublishBlocked")]
internal sealed class WorkerRunPublishBlockedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    TimeProvider timeProvider,
    ILogger<WorkerRunPublishBlockedHandler> logger) : IIntegrationEventHandler<WorkerRunPublishBlocked>
{
    public async Task HandleAsync(WorkerRunPublishBlocked @event, CancellationToken cancellationToken)
    {
        IssueId issueId = IssueId.From(@event.IssueId);
        DateTimeOffset blockedAt = timeProvider.GetUtcNow();

        Issue? issue = await db.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

        switch (issue)
        {
            case InProgressIssue inProgress when inProgress.WorkerRunId == @event.WorkerRunId:
                PublishBlockedIssue blocked = inProgress.MarkPublishBlocked(@event.BranchName, blockedAt);
                await db.TransitionAsync(inProgress, blocked, domainEventDispatcher, cancellationToken);
                return;

            case RevisionInProgressIssue revision when revision.WorkerRunId == @event.WorkerRunId:
                PublishBlockedIssue revisionBlocked = revision.MarkPublishBlocked(blockedAt);
                await db.TransitionAsync(revision, revisionBlocked, domainEventDispatcher, cancellationToken);
                return;

            default:
                logger.LogWarning(
                    "WorkerRunPublishBlocked for issue {IssueId} run {EventRunId} does not match an issue running that run (state: {State}); ignoring.",
                    @event.IssueId,
                    @event.WorkerRunId,
                    issue?.GetType().Name ?? "not found");
                return;
        }
    }
}
