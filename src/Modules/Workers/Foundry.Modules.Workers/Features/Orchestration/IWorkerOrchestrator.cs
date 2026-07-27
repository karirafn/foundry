using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Orchestration;

internal interface IWorkerOrchestrator
{
    Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken);

    Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken);

    Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken);

    Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken);

    IAsyncEnumerable<string> StreamLogsAsync(string containerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(CancellationToken cancellationToken);

    Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken);

    Task StopContainerAsync(string containerId, CancellationToken cancellationToken);

    Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken);
}
