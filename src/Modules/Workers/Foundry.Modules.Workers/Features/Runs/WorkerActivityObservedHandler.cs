using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Runs;

internal sealed class WorkerActivityObservedHandler(IWorkerActivityBroadcaster broadcaster)
    : IDomainEventHandler<WorkerActivityObserved>
{
    public Task HandleAsync(WorkerActivityObserved @event, CancellationToken cancellationToken)
    {
        WorkerActivity activity = new(
            WorkerRunId: @event.WorkerRunId.Value,
            IssueId: @event.IssueId.Value,
            LastActivityAt: @event.LastActivityAt,
            CommitCount: @event.CommitCount);

        return broadcaster.BroadcastActivityAsync(activity, cancellationToken);
    }
}
