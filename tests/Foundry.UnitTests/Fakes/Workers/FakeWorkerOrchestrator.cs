using System.Runtime.CompilerServices;

using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Login;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// Scriptable in-memory fake of <see cref="IWorkerOrchestrator"/> for unit-testing
/// the login state machine with zero Docker.
/// <para>
/// Supply <paramref name="logLines"/> to script what <see cref="StreamLogsAsync"/> yields.
/// Use the fluent With* builders to script specific orchestrator responses.
/// </para>
/// </summary>
internal sealed class FakeWorkerOrchestrator(IEnumerable<string>? logLines = null) : IWorkerOrchestrator
{
    private readonly IReadOnlyList<string> _logLines = logLines?.ToList() ?? [];

    private WorkerStatus _containerStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
    private Result<AccountIdentity> _authStatusResult =
        Result<AccountIdentity>.Ok(new AccountIdentity("user@example.com", "Test Org", "pro"));
    private Result<ContainerId> _startLoginResult =
        Result<ContainerId>.Ok(ContainerId.From("fake-login-container"));
    private IReadOnlyList<ContainerId> _loginContainerIds = [];

    // When true, StreamLogsAsync yields _logLines then blocks until cancellation (does not close).
    private bool _blockAfterLines;

    // Gate for DeliverLoginCodeAsync — when set, Deliver blocks until the TCS is completed.
    private TaskCompletionSource? _deliverGate;

    public int DeliverLoginCodeCallCount { get; private set; }
    public int StopContainerCallCount { get; private set; }
    public int RemoveContainerCallCount { get; private set; }
    public int GetCredentialVolumeAuthStatusCallCount { get; private set; }
    public string? LastDeliveredCode { get; private set; }

    /// <summary>Scripts <see cref="GetStatusAsync"/> to return a container that exited with the given code.</summary>
    public FakeWorkerOrchestrator WithExitedContainer(int exitCode = 0)
    {
        _containerStatus = new WorkerStatus(
            IsRunning: false,
            ExitCode: exitCode,
            FinishedAt: DateTimeOffset.UtcNow);
        return this;
    }

    /// <summary>Scripts <see cref="GetAuthStatusAsync"/> to return a failure result.</summary>
    public FakeWorkerOrchestrator WithAuthStatusFailure(Error error)
    {
        _authStatusResult = Result<AccountIdentity>.Fail(error);
        return this;
    }

    /// <summary>Scripts <see cref="GetAuthStatusAsync"/> to return a specific identity.</summary>
    public FakeWorkerOrchestrator WithAuthStatusIdentity(AccountIdentity identity)
    {
        _authStatusResult = Result<AccountIdentity>.Ok(identity);
        return this;
    }

    /// <summary>Scripts <see cref="StartLoginContainerAsync"/> to return a failure.</summary>
    public FakeWorkerOrchestrator WithStartLoginFailure(Error error)
    {
        _startLoginResult = Result<ContainerId>.Fail(error);
        return this;
    }

    /// <summary>Scripts <see cref="ListLoginContainersByLabelAsync"/> to return orphaned container IDs.</summary>
    public FakeWorkerOrchestrator WithOrphanedLoginContainers(params string[] containerIds)
    {
        _loginContainerIds = containerIds
            .Select(ContainerId.From)
            .ToList();
        return this;
    }

    /// <summary>
    /// Configures <see cref="StreamLogsAsync"/> to yield <paramref name="logLines"/> and then
    /// block indefinitely until the cancellation token fires (simulating a CLI that re-prompts
    /// after an invalid code without exiting).
    /// </summary>
    public FakeWorkerOrchestrator WithBlockingStreamAfterLines()
    {
        _blockAfterLines = true;
        return this;
    }

    /// <summary>
    /// Arms a gate that makes <see cref="DeliverLoginCodeAsync"/> block until
    /// <see cref="ReleaseDeliver"/> is called. Use this to deterministically reproduce the
    /// race where the background task disposes <c>_sessionTimeoutCts</c> while
    /// <c>SubmitCodeAsync</c> is still inside <c>DeliverLoginCodeAsync</c>.
    /// </summary>
    public FakeWorkerOrchestrator WithBlockingDeliver()
    {
        _deliverGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return this;
    }

    /// <summary>
    /// Releases the gate armed by <see cref="WithBlockingDeliver"/>, allowing
    /// <see cref="DeliverLoginCodeAsync"/> to return.
    /// </summary>
    public void ReleaseDeliver() => _deliverGate?.TrySetResult();

    public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<Result<ContainerId>> StartAsync(
        WorkerContainerSpec spec,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<ContainerId>.Ok(ContainerId.From("fake-login-container")));

    public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
        => Task.FromResult<WorkerStatus?>(_containerStatus);

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

        if (_blockAfterLines)
        {
            // Block until the cancellation token fires — simulates a CLI that stays alive
            // (e.g., after emitting "Invalid code", it re-prompts and never exits).
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
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

    public Task<Result<ContainerId>> StartLoginContainerAsync(
        LoginContainerSpec spec,
        CancellationToken cancellationToken)
        => Task.FromResult(_startLoginResult);

    public async Task DeliverLoginCodeAsync(string containerId, string code, CancellationToken cancellationToken)
    {
        DeliverLoginCodeCallCount++;
        LastDeliveredCode = code;

        if (_deliverGate is not null)
        {
            await _deliverGate.Task.WaitAsync(cancellationToken);
        }
    }

    public Task<Result<AccountIdentity>> GetAuthStatusAsync(string containerId, CancellationToken cancellationToken)
    {
        // Faithfully model Docker's "container is not running" error — docker exec on a stopped
        // container throws, so callers must not exec against the login container after it exits.
        if (!_containerStatus.IsRunning)
        {
            throw new InvalidOperationException("container is not running");
        }

        return Task.FromResult(_authStatusResult);
    }

    public Task<Result<AccountIdentity>> GetCredentialVolumeAuthStatusAsync(CancellationToken cancellationToken)
    {
        GetCredentialVolumeAuthStatusCallCount++;
        return Task.FromResult(_authStatusResult);
    }

    public Task<IReadOnlyList<ContainerId>> ListLoginContainersByLabelAsync(CancellationToken cancellationToken)
        => Task.FromResult(_loginContainerIds);

    public Task SeedOnboardingAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
