using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Features.StateChanges;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

namespace Foundry.Modules.Issues.Features.WorkerReactions;

/// <summary>
/// A run's publish phase is part of its issue's summary, so a phase change rebroadcasts the summary the same way a
/// state change does, without changing the issue's state.
/// </summary>
[IntegrationEventHandlerIdentity("Issues.WorkerRunPublishPhaseChanged")]
internal sealed class WorkerRunPublishPhaseChangedHandler(IssueStateChangedHandler stateChangedHandler)
    : IIntegrationEventHandler<WorkerRunPublishPhaseChanged>
{
    public Task HandleAsync(WorkerRunPublishPhaseChanged @event, CancellationToken cancellationToken)
        => stateChangedHandler.BroadcastSummaryAsync(IssueId.From(@event.IssueId), cancellationToken);
}
