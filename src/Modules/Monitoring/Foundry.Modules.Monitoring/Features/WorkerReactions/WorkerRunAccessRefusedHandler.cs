using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.WorkerReactions;

/// <summary>
/// Resets a repository's <c>Granted</c> write probe when a run reports its token was refused, so dispatch stops in this
/// save and the next poll re-probes (ADR 0054). It never calls the provider: the poller owns when a probe is due.
/// </summary>
[IntegrationEventHandlerIdentity("Monitoring.WorkerRunAccessRefused")]
internal sealed class WorkerRunAccessRefusedHandler(
    DbContext dbContext,
    ILogger<WorkerRunAccessRefusedHandler> logger)
    : IIntegrationEventHandler<WorkerRunAccessRefused>
{
    public async Task HandleAsync(WorkerRunAccessRefused @event, CancellationToken cancellationToken)
    {
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.From(@event.MonitoredRepositoryId);

        if (await dbContext.Set<MonitoredRepository>()
                .FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken)
            is not MonitoredRepository repository)
        {
            logger.LogInformation(
                "Run {WorkerRunId} reported a refused token for repository {RepositoryId}, which is not monitored.",
                @event.WorkerRunId.Value,
                @event.MonitoredRepositoryId);
            return;
        }

        if (!repository.ResetGrantedWriteProbe())
        {
            logger.LogDebug(
                "Run {WorkerRunId} reported a refused token for repository {Slug}, whose write probe is not Granted.",
                @event.WorkerRunId.Value,
                repository.Slug);
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Run {WorkerRunId} reported a refused token for repository {Slug}; its write probe re-runs on the next poll.",
            @event.WorkerRunId.Value,
            repository.Slug);
    }
}
