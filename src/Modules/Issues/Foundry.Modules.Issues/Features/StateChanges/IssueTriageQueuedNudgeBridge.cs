using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Features.StateChanges;

/// <summary>
/// Bridges the <see cref="IssueTriageQueued"/> domain event to the <see cref="IssueEnteredTriageQueue"/>
/// integration event, so Monitoring can nudge its dependency pass. The event is enqueued during domain-event
/// dispatch and harvested into the outbox on the trailing save.
/// </summary>
internal sealed class IssueTriageQueuedNudgeBridge(IIntegrationEventDispatcher integrationEventDispatcher)
    : IDomainEventHandler<IssueTriageQueued>
{
    public Task HandleAsync(IssueTriageQueued @event, CancellationToken cancellationToken)
        => integrationEventDispatcher.DispatchAsync(
            [new IssueEnteredTriageQueue(@event.MonitoredRepositoryId)],
            cancellationToken);
}
