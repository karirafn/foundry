using System.Runtime.CompilerServices;

using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// Scriptable in-memory fake of <see cref="IWorkerOrchestrator"/> for unit-testing
/// the login state machine with zero Docker.
/// <para>
/// Supply <paramref name="logLines"/> to script what <see cref="StreamLogsAsync"/> yields.
/// Later steps extend the scriptable surface as new orchestrator methods are added.
/// </para>
/// </summary>
internal sealed class FakeWorkerOrchestrator(IEnumerable<string>? logLines = null) : IWorkerOrchestrator
{
    private readonly IReadOnlyList<string> _logLines = logLines?.ToList() ?? [];

    public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<Result<ContainerId>> StartAsync(
        WorkerContainerSpec spec,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<ContainerId>.Ok(ContainerId.From("fake-login-container")));

    public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
        => Task.FromResult<WorkerStatus?>(new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null));

    public async IAsyncEnumerable<string> StreamLogsAsync(
        string containerId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (string line in _logLines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield return line;
        }
    }

    public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

    public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
