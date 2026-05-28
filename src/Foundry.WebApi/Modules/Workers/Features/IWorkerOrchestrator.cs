using Foundry.WebApi.Modules.Workers.Domain;
using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Workers.Features;

public interface IWorkerOrchestrator
{
    Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken);

    Task StopAsync(string containerId, CancellationToken cancellationToken);

    Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken);

    IAsyncEnumerable<string> StreamLogsAsync(string containerId, CancellationToken cancellationToken);
}
