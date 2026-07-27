using System.Runtime.CompilerServices;

using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Shared;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// Scriptable in-memory fake of <see cref="IWorkerOrchestrator"/> for unit-testing
/// the worker dispatch service with zero Docker.
/// </summary>
internal sealed class FakeWorkerOrchestrator : IWorkerOrchestrator
{
    private WorkerStatus _containerStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
    private WorkerStatusProbe? _probeOverride;

    public int StopContainerCallCount { get; private set; }
    public int RemoveContainerCallCount { get; private set; }

    /// <summary>Scripts <see cref="GetStatusAsync"/> to return a container that exited with the given code.</summary>
    public FakeWorkerOrchestrator WithExitedContainer(int exitCode = 0)
    {
        _containerStatus = new WorkerStatus(
            IsRunning: false,
            ExitCode: exitCode,
            FinishedAt: DateTimeOffset.UtcNow);
        return this;
    }

    /// <summary>Scripts <see cref="GetStatusAsync"/> to return an <see cref="WorkerStatusProbe.Unreachable"/> probe,
    /// simulating a Docker daemon connectivity failure.</summary>
    public FakeWorkerOrchestrator WithUnreachableDaemon()
    {
        _probeOverride = new WorkerStatusProbe.Unreachable();
        return this;
    }

    public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<Result<ContainerId>> StartAsync(
        WorkerContainerSpec spec,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<ContainerId>.Ok(ContainerId.From("fake-worker-container")));

    public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
        => Task.FromResult(_probeOverride ?? (WorkerStatusProbe)new WorkerStatusProbe.Available(_containerStatus));

    public async IAsyncEnumerable<string> StreamLogsAsync(
        string containerId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

    public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        StopContainerCallCount++;
        return Task.CompletedTask;
    }

    public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        RemoveContainerCallCount++;
        return Task.CompletedTask;
    }
}
