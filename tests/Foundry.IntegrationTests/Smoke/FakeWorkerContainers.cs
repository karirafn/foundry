using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared;

namespace Foundry.IntegrationTests.Smoke;

/// <summary>
/// Worker containers with no Docker behind them. A started container runs until the test exits it, after
/// which it reports its exit code and returns its log.
/// </summary>
internal sealed class FakeWorkerContainers : IWorkerOrchestrator
{
    private const string WorkerRunIdLabel = "foundry.worker-run-id";

    private readonly ConcurrentDictionary<string, Container> _containers = new();
    private readonly ConcurrentQueue<WorkerContainerSpec> _startedSpecs = new();

    public IReadOnlyCollection<WorkerContainerSpec> StartedSpecs => _startedSpecs;

    /// <summary>Exits every running container with <paramref name="exitCode"/>, leaving <paramref name="log"/> as its output.</summary>
    public void ExitAll(int exitCode, string log)
    {
        foreach (Container container in _containers.Values)
        {
            container.Exit(exitCode, log);
        }
    }

    public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);

        string containerId = $"smoke-container-{_containers.Count + 1}";
        _containers[containerId] = new Container(WorkerRunId.From(Guid.Parse(spec.Labels[WorkerRunIdLabel])));
        _startedSpecs.Enqueue(spec);
        return Task.FromResult(Result<ContainerId>.Ok(ContainerId.From(containerId)));
    }

    public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
        => Task.FromResult<WorkerStatusProbe>(_containers.TryGetValue(containerId, out Container? container)
            ? new WorkerStatusProbe.Available(container.Status)
            : new WorkerStatusProbe.NotFound());

    public async IAsyncEnumerable<string> StreamLogsAsync(
        string containerId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_containers.TryGetValue(containerId, out Container? container))
        {
            yield break;
        }

        string log = await container.Exited.WaitAsync(cancellationToken);

        foreach (string line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return line;
        }
    }

    public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>(
            [.. _containers.Select(pair => (ContainerId.From(pair.Key), pair.Value.WorkerRunId))]);

    public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
        => Task.FromResult(_containers.TryGetValue(containerId, out Container? container) ? container.Log : null);

    public Task StopContainerAsync(string containerId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReapOrphanedDataRootVolumesAsync(IReadOnlySet<WorkerRunId> activeRunIds, CancellationToken cancellationToken)
        => Task.CompletedTask;

    private sealed class Container(WorkerRunId workerRunId)
    {
        private readonly TaskCompletionSource<string> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile WorkerStatus _status = new(IsRunning: true, ExitCode: null, FinishedAt: null);

        public WorkerRunId WorkerRunId { get; } = workerRunId;

        public WorkerStatus Status => _status;

        public Task<string> Exited => _exited.Task;

        public string? Log => _exited.Task.IsCompletedSuccessfully ? _exited.Task.Result : null;

        public void Exit(int exitCode, string log)
        {
            // The log is in place before the status reports the exit, so a monitor never reads an exited container without it.
            _exited.TrySetResult(log);
            _status = new WorkerStatus(IsRunning: false, exitCode, DateTimeOffset.UtcNow);
        }
    }
}
