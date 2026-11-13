using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Publishing;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Runs;

/// <summary>
/// Discards a run holding unpublished work once its issue completed because the provider issue closed (#909): that work
/// will never be published, so its Hand-off volume is deleted now rather than at its time to live. Unlike Discard run, a
/// publish still retrying is discarded too. A run no longer discardable (already discarded or expired, back publishing
/// for a retry, or gone) is left as it is, which also makes redelivery a no-op.
/// </summary>
[IntegrationEventHandlerIdentity("Workers.IssueClosedWithUnpublishedRun")]
internal sealed class IssueClosedWithUnpublishedRunHandler(
    DbContext dbContext,
    IWorkerOrchestrator orchestrator,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<IssueClosedWithUnpublishedRunHandler> logger) : IIntegrationEventHandler<IssueClosedWithUnpublishedRun>
{
    public async Task HandleAsync(IssueClosedWithUnpublishedRun @event, CancellationToken cancellationToken)
    {
        WorkerRun? run = await dbContext.Set<WorkerRun>()
            .FirstOrDefaultAsync(r => r.Id == @event.WorkerRunId, cancellationToken);

        if (run is not UnpublishedRun unpublished || run is not IDiscardableRun discardable)
        {
            logger.LogInformation(
                "Worker run {WorkerRunId} holds no discardable unpublished work ({RunState}); its issue's close leaves it as it is.",
                @event.WorkerRunId,
                run?.GetType().Name ?? "not found");
            return;
        }

        FailedRun discarded = discardable.DiscardOnIssueClosed();

        await RunDiscarder.DiscardAsync(
            dbContext,
            orchestrator,
            domainEventDispatcher,
            unpublished,
            discarded,
            cancellationToken);

        logger.LogInformation(
            "Discarded worker run {WorkerRunId}: its issue was closed before its work was published.",
            @event.WorkerRunId);
    }
}
