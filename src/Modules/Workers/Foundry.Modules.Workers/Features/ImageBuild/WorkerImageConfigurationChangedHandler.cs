using Foundry.Modules.Settings.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.ImageBuild;

[IntegrationEventHandlerIdentity("Workers.WorkerImageConfigurationChanged")]
internal sealed class WorkerImageConfigurationChangedHandler(
    IWorkerImageRebuildQueue rebuildQueue)
    : IIntegrationEventHandler<WorkerImageConfigurationChanged>
{
    public Task HandleAsync(WorkerImageConfigurationChanged @event, CancellationToken cancellationToken)
    {
        rebuildQueue.RequestImmediateRebuild();
        return Task.CompletedTask;
    }
}
