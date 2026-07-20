using Foundry.Modules.Workers.Domain.Events;
using Foundry.Shared;

using WorkerRunFailedIntegration = Foundry.Modules.Workers.Contracts.WorkerRunFailed;

namespace Foundry.Modules.Workers.Features;

internal sealed class WorkerRunFailedBridgeHandler(IIntegrationEventDispatcher integrationEventDispatcher)
    : IDomainEventHandler<WorkerRunFailed>
{
    public Task HandleAsync(WorkerRunFailed @event, CancellationToken cancellationToken)
    {
        WorkerRunFailedIntegration integrationEvent = new(
            @event.WorkerRunId.Value,
            @event.IssueId.Value,
            @event.ReasonDescription,
            @event.Category,
            @event.BranchName);

        return integrationEventDispatcher.DispatchAsync([integrationEvent], cancellationToken);
    }
}
