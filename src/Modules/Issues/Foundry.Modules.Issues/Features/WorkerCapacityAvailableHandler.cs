using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features;

internal sealed class WorkerCapacityAvailableHandler(
    DbContext db,
    IIntegrationEventDispatcher integrationEventDispatcher) : IIntegrationEventHandler<WorkerCapacityAvailable>
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

        var dispatchData = await db.Set<MonitoredRepository>()
            .Where(r => r.Id == queued.MonitoredRepositoryId)
            .Join(
                db.Set<Account>(),
                r => r.AccountId,
                a => a.Id,
                (r, a) => new { r.Slug, a.SecretKeyName, a.BaseUrl })
            .FirstOrDefaultAsync(cancellationToken);

        if (dispatchData is null)
        {
            return;
        }

        InProgressIssue inProgress = queued.Claim(@event.WorkerRunId);
        await db.TransitionAsync(queued, inProgress, cancellationToken);

        Uri cloneUrl = new(dispatchData.BaseUrl, $"{dispatchData.Slug}.git");

        ClaimedIssueDispatch dispatch = new(
            inProgress.Id,
            @event.WorkerRunId,
            inProgress.IssueNumber,
            inProgress.Title,
            inProgress.Body,
            dispatchData.Slug.ToString(),
            cloneUrl,
            dispatchData.SecretKeyName);

        await integrationEventDispatcher.DispatchAsync(
            [new IssueClaimed(dispatch)],
            cancellationToken);
    }
}
