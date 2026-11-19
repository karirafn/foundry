using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Repositories;

/// <summary>
/// Bridges <see cref="MonitoredRepositoryRemovedDomainEvent"/> to <see cref="MonitoredRepositoryRemoved"/>, so Issues and
/// Workers clean up after the repository. The event is enqueued during domain-event dispatch and harvested into the
/// outbox on the trailing save, in the same transaction as the removal.
/// </summary>
internal sealed class MonitoredRepositoryRemovedBridge(IIntegrationEventDispatcher integrationEventDispatcher)
    : IDomainEventHandler<MonitoredRepositoryRemovedDomainEvent>
{
    public Task HandleAsync(MonitoredRepositoryRemovedDomainEvent @event, CancellationToken cancellationToken)
        => integrationEventDispatcher.DispatchAsync(
            [new MonitoredRepositoryRemoved(@event.RepositoryId, @event.RemovedAt)],
            cancellationToken);
}
