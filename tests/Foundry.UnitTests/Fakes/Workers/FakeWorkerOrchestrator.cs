using System.Runtime.CompilerServices;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
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
    private IReadOnlyList<string> _logLines = [];
    private Exception? _logStreamFault;

    public int StreamLogsCallCount { get; private set; }

    public int StopContainerCallCount { get; private set; }
    public int RemoveContainerCallCount { get; private set; }
    public int ReapOrphanedDataRootVolumesCallCount { get; private set; }
    public IReadOnlySet<WorkerRunId>? LastReapActiveRunIds { get; private set; }

    private readonly List<WorkerRunId> _deletedHandOffVolumes = [];

    /// <summary>The runs whose Hand-off volume was deleted, in order.</summary>
    public IReadOnlyList<WorkerRunId> DeletedHandOffVolumes => _deletedHandOffVolumes;

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

    /// <summary>Scripts <see cref="StreamLogsAsync"/> to yield the given container log lines, then end.</summary>
    public FakeWorkerOrchestrator WithLogLines(params string[] lines)
    {
        _logLines = lines;
        return this;
    }

    /// <summary>Scripts <see cref="StreamLogsAsync"/> to throw after yielding the scripted lines.</summary>
    public FakeWorkerOrchestrator WithLogStreamFault(Exception fault)
    {
        _logStreamFault = fault;
        return this;
    }

    public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task DeleteHandOffVolumeAsync(WorkerRunId runId, CancellationToken cancellationToken)
    {
        _deletedHandOffVolumes.Add(runId);
        return Task.CompletedTask;
    }

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
        StreamLogsCallCount++;

        foreach (string line in _logLines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return line;
        }

        if (_logStreamFault is not null)
        {
            throw _logStreamFault;
        }
    }

    public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

    public Task<string?> GetFullLogAsync(string containerId, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

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

    public Task ReapOrphanedDataRootVolumesAsync(
        IReadOnlySet<WorkerRunId> activeRunIds,
        CancellationToken cancellationToken)
    {
        ReapOrphanedDataRootVolumesCallCount++;
        LastReapActiveRunIds = activeRunIds;
        return Task.CompletedTask;
    }
}
