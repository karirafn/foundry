using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Publishing;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Runs;

/// <summary>
/// Discards every run of a removed repository that holds unpublished work (ADR 0117): that work can never be published,
/// so each Hand-off volume is deleted now rather than at its time to live. Every other run is kept for per-repository
/// statistics. A run no longer discardable (a publish in flight, or already discarded) is left as it is, which also makes
/// redelivery a no-op. Each discard commits on its own, so a redelivery after a partial run finds only the runs still
/// unpublished.
/// </summary>
[IntegrationEventHandlerIdentity("Workers.MonitoredRepositoryRemoved")]
internal sealed class MonitoredRepositoryRemovedHandler(
    DbContext dbContext,
    IWorkerOrchestrator orchestrator,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<MonitoredRepositoryRemovedHandler> logger) : IIntegrationEventHandler<MonitoredRepositoryRemoved>
{
    public async Task HandleAsync(MonitoredRepositoryRemoved @event, CancellationToken cancellationToken)
    {
        List<UnpublishedRun> runs = await dbContext.Set<WorkerRun>()
            .OfType<UnpublishedRun>()
            .Where(r => r.MonitoredRepositoryId == @event.RepositoryId)
            .ToListAsync(cancellationToken);

        foreach (UnpublishedRun run in runs)
        {
            if (run is not IDiscardableRun discardable)
            {
                logger.LogInformation(
                    "Worker run {WorkerRunId} of removed repository {RepositoryId} is not discardable ({RunState}); leaving it as it is.",
                    run.Id,
                    @event.RepositoryId,
                    run.GetType().Name);
                continue;
            }

            await RunDiscarder.DiscardAsync(
                dbContext,
                orchestrator,
                domainEventDispatcher,
                run,
                discardable.DiscardOnRepositoryRemoved(),
                cancellationToken);

            logger.LogInformation(
                "Discarded worker run {WorkerRunId}: its repository {RepositoryId} was removed before its work was published.",
                run.Id,
                @event.RepositoryId);
        }
    }
}
