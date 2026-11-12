using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Features.StateChanges;

/// <summary>
/// Bridges the <see cref="TriageBlockersNamed"/> domain event to the <see cref="TriageBlockersFound"/> integration
/// event, so Monitoring links each named blocker on the provider (ADR 0103).
/// </summary>
internal sealed class TriageBlockersFoundBridge(IIntegrationEventDispatcher integrationEventDispatcher)
    : IDomainEventHandler<TriageBlockersNamed>
{
    public Task HandleAsync(TriageBlockersNamed @event, CancellationToken cancellationToken)
        => integrationEventDispatcher.DispatchAsync(
            [
                new TriageBlockersFound(
                    @event.IssueId.Value,
                    @event.MonitoredRepositoryId,
                    @event.IssueNumber,
                    @event.WorkerRunId,
                    @event.Blockers),
            ],
            cancellationToken);
}
