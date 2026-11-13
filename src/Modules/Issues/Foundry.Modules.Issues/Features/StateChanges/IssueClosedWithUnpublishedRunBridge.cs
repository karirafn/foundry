using Foundry.Shared;

using DomainEvent = Foundry.Modules.Issues.Domain.Events.IssueClosedWithUnpublishedRun;
using IntegrationEvent = Foundry.Modules.Issues.Contracts.Events.IssueClosedWithUnpublishedRun;

namespace Foundry.Modules.Issues.Features.StateChanges;

/// <summary>
/// Bridges the <see cref="DomainEvent"/> domain event to the <see cref="IntegrationEvent"/> integration event, so
/// Workers discards the run and deletes its Hand-off volume. The event is enqueued during domain-event dispatch and
/// harvested into the outbox on the trailing save, in the same transaction as the issue's completion.
/// </summary>
internal sealed class IssueClosedWithUnpublishedRunBridge(IIntegrationEventDispatcher integrationEventDispatcher)
    : IDomainEventHandler<DomainEvent>
{
    public Task HandleAsync(DomainEvent @event, CancellationToken cancellationToken)
        => integrationEventDispatcher.DispatchAsync([new IntegrationEvent(@event.WorkerRunId)], cancellationToken);
}
