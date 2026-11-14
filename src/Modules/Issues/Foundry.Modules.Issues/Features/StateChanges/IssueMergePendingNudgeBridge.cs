using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Features.StateChanges;

/// <summary>
/// Bridges the <see cref="IssueMergePending"/> domain event to the <see cref="IssueEnteredMergePending"/>
/// integration event, so Monitoring can nudge its merge step (ADR 0105). The event is enqueued during
/// domain-event dispatch and harvested into the outbox on the trailing save.
/// </summary>
internal sealed class IssueMergePendingNudgeBridge(IIntegrationEventDispatcher integrationEventDispatcher)
    : IDomainEventHandler<IssueMergePending>
{
    public Task HandleAsync(IssueMergePending @event, CancellationToken cancellationToken)
        => integrationEventDispatcher.DispatchAsync(
            [new IssueEnteredMergePending(@event.MonitoredRepositoryId, @event.IssueNumber, @event.EnteredMergePendingAt)],
            cancellationToken);
}
