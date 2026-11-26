using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Features.StateChanges;

/// <summary>
/// Bridges the <see cref="TriageDetailsDecided"/> domain event to the <see cref="TriageDecisionsRecorded"/> integration
/// event, so Monitoring posts the decisions on the issue thread (ADR 0123).
/// </summary>
internal sealed class TriageDecisionsRecordedBridge(IIntegrationEventDispatcher integrationEventDispatcher)
    : IDomainEventHandler<TriageDetailsDecided>
{
    public Task HandleAsync(TriageDetailsDecided @event, CancellationToken cancellationToken)
        => integrationEventDispatcher.DispatchAsync(
            [
                new TriageDecisionsRecorded(
                    @event.IssueId.Value,
                    @event.MonitoredRepositoryId,
                    @event.IssueNumber,
                    @event.Decisions,
                    @event.Release,
                    @event.Demand,
                    @event.DemandReason),
            ],
            cancellationToken);
}
