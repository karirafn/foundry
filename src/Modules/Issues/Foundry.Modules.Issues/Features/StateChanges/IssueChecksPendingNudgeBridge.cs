using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Features.StateChanges;

/// <summary>
/// Bridges the <see cref="IssueChecksPending"/> domain event to the <see cref="IssueEnteredChecksPending"/>
/// integration event, so Monitoring can schedule the checks-gate deadline nudges (ADR 0085). The event is
/// enqueued during domain-event dispatch and harvested into the outbox on the trailing save.
/// </summary>
internal sealed class IssueChecksPendingNudgeBridge(IIntegrationEventDispatcher integrationEventDispatcher)
    : IDomainEventHandler<IssueChecksPending>
{
    public Task HandleAsync(IssueChecksPending @event, CancellationToken cancellationToken)
        => integrationEventDispatcher.DispatchAsync(
            [new IssueEnteredChecksPending(@event.MonitoredRepositoryId, @event.IssueNumber, @event.EnteredChecksPendingAt)],
            cancellationToken);
}
