using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Features.StateChanges;

/// <summary>
/// Bridges the <see cref="IssueAwaitingInput"/> domain event to the <see cref="IssueEnteredAwaitingInput"/> integration
/// event, so Monitoring can nudge its awaiting-input step to post the questions (ADR 0096).
/// </summary>
internal sealed class IssueAwaitingInputNudgeBridge(IIntegrationEventDispatcher integrationEventDispatcher)
    : IDomainEventHandler<IssueAwaitingInput>
{
    public Task HandleAsync(IssueAwaitingInput @event, CancellationToken cancellationToken)
        => integrationEventDispatcher.DispatchAsync(
            [new IssueEnteredAwaitingInput(@event.MonitoredRepositoryId, @event.IssueNumber)],
            cancellationToken);
}
