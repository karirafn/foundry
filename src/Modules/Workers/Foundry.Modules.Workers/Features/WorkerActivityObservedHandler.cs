using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features;

internal sealed class WorkerActivityObservedHandler(IWorkerActivityBroadcaster broadcaster)
    : IDomainEventHandler<WorkerActivityObserved>
{
    public Task HandleAsync(WorkerActivityObserved @event, CancellationToken cancellationToken)
    {
        WorkerActivity activity = new(
            WorkerRunId: @event.WorkerRunId.Value,
            IssueId: @event.IssueId.Value,
            LastActivityAt: @event.LastActivityAt,
            NewCommitSha: @event.NewCommitMarker?.Sha,
            NewCommitMessage: @event.NewCommitMarker?.Message);

        return broadcaster.BroadcastActivityAsync(activity, cancellationToken);
    }
}
