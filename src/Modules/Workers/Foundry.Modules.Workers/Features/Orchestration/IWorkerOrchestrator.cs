using Foundry.Modules.Workers.Contracts;
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

    /// <summary>
    /// Stops and removes a worker whose run is publishing (ADR 0100): its per-run volumes go with it except the Hand-off
    /// volume, which holds the Run bundle until the run is published or discarded.
    /// </summary>
    /// <remarks>Defaults to <see cref="StopAndRemoveAsync"/> for orchestrators without per-run volumes.</remarks>
    Task StopAndRemoveKeepingHandOffAsync(string containerId, CancellationToken cancellationToken) =>
        StopAndRemoveAsync(containerId, cancellationToken);

    /// <summary>Deletes a run's Hand-off volume once nothing needs its Run bundle any more. Best-effort.</summary>
    /// <remarks>Orchestrators without per-run volumes keep this default, which has nothing to delete.</remarks>
    Task DeleteHandOffVolumeAsync(WorkerRunId runId, CancellationToken cancellationToken) => Task.CompletedTask;

    Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken);

    IAsyncEnumerable<string> StreamLogsAsync(string containerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(CancellationToken cancellationToken);

    Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken);

    /// <summary>The container's whole redacted log, not limited to a tail; <c>null</c> when the container is gone.</summary>
    Task<string?> GetFullLogAsync(string containerId, CancellationToken cancellationToken);

    Task StopContainerAsync(string containerId, CancellationToken cancellationToken);

    Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken);

    /// <summary>Removes every per-run volume (data-root and Hand-off) whose run is not in <paramref name="activeRunIds"/>.</summary>
    Task ReapOrphanedDataRootVolumesAsync(IReadOnlySet<WorkerRunId> activeRunIds, CancellationToken cancellationToken);
}
