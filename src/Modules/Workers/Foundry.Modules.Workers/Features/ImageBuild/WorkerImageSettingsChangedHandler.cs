using Foundry.Modules.Settings.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.ImageBuild;

[IntegrationEventHandlerIdentity("Workers.WorkerImageSettingsChanged")]
internal sealed class WorkerImageSettingsChangedHandler(
    IWorkerImageRebuildQueue rebuildQueue)
    : IIntegrationEventHandler<WorkerImageSettingsChanged>
{
    public Task HandleAsync(WorkerImageSettingsChanged @event, CancellationToken cancellationToken)
    {
        rebuildQueue.RequestImmediateRebuild();
        return Task.CompletedTask;
    }
}
