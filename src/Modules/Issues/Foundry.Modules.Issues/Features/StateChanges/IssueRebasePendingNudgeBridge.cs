using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Features.StateChanges;

/// <summary>
/// Bridges the <see cref="IssueRebasePending"/> domain event to the <see cref="IssueEnteredRebasePending"/>
/// integration event, so Monitoring can nudge its rebase step (ADR 0087). The event is enqueued during
/// domain-event dispatch and harvested into the outbox on the trailing save.
/// </summary>
internal sealed class IssueRebasePendingNudgeBridge(IIntegrationEventDispatcher integrationEventDispatcher)
    : IDomainEventHandler<IssueRebasePending>
{
    public Task HandleAsync(IssueRebasePending @event, CancellationToken cancellationToken)
        => integrationEventDispatcher.DispatchAsync(
            [new IssueEnteredRebasePending(@event.MonitoredRepositoryId, @event.IssueNumber, @event.EnteredRebasePendingAt)],
            cancellationToken);
}
