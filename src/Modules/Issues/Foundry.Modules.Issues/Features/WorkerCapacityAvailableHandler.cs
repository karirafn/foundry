using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features;

internal sealed class WorkerCapacityAvailableHandler(
    DbContext db,
    IRepositoryDispatchQueries repositoryDispatchQueries,
    IIntegrationEventDispatcher integrationEventDispatcher,
    ILogger<WorkerCapacityAvailableHandler> logger) : IIntegrationEventHandler<WorkerCapacityAvailable>
{
    public async Task HandleAsync(WorkerCapacityAvailable @event, CancellationToken cancellationToken)
    {
        // Claim the oldest queued issue (FIFO).
        QueuedIssue? queued = await db.Set<QueuedIssue>()
            .FirstOrDefaultAsync(cancellationToken);

        if (queued is null)
        {
            return;
        }

        RepositoryDispatchInfo? dispatchInfo = await repositoryDispatchQueries.GetDispatchInfoAsync(
            queued.MonitoredRepositoryId,
            cancellationToken);

        if (dispatchInfo is null)
        {
            logger.LogWarning(
                "Could not find dispatch info for repository {RepositoryId}; issue #{IssueNumber} not claimed.",
                queued.MonitoredRepositoryId,
                queued.IssueNumber);
            return;
        }

        InProgressIssue inProgress = queued.Claim(@event.WorkerRunId);
        await db.TransitionAsync(queued, inProgress, cancellationToken);

        ClaimedIssueDispatch dispatch = new(
            inProgress.Id,
            @event.WorkerRunId,
            inProgress.IssueNumber,
            inProgress.Title,
            inProgress.Body,
            dispatchInfo.RepositorySlug,
            dispatchInfo.CloneUrl,
            dispatchInfo.AccountSecretKeyName);

        await integrationEventDispatcher.DispatchAsync(
            [new IssueClaimed(dispatch)],
            cancellationToken);
    }
}
