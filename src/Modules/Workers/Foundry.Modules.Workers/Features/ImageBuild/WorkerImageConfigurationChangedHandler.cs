using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Events;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.ImageBuild;

internal sealed class WorkerImageConfigurationChangedHandler(
    IWorkerImageRebuildQueue rebuildQueue)
    : IIntegrationEventHandler<WorkerImageConfigurationChanged>
{
    public Task HandleAsync(WorkerImageConfigurationChanged @event, CancellationToken cancellationToken)
    {
        rebuildQueue.TryEnqueue();
        return Task.CompletedTask;
    }
}
