using System.Runtime.CompilerServices;

using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Infrastructure;
using Foundry.Shared;

namespace Foundry.UnitTests.Fakes.Credentials;

/// <summary>
/// Scriptable in-memory fake of <see cref="ICredentialsOrchestrator"/> for unit-testing
/// the login state machine with zero Docker.
/// <para>
/// Supply <paramref name="logLines"/> to script what <see cref="StreamLogsAsync"/> yields.
/// Use the fluent With* builders to script specific orchestrator responses.
/// </para>
/// </summary>
internal sealed class FakeCredentialsOrchestrator(IEnumerable<string>? logLines = null) : ICredentialsOrchestrator
{
    private readonly IReadOnlyList<string> _logLines = logLines?.ToList() ?? [];

    private Result<AccountIdentity> _authStatusResult =
        Result<AccountIdentity>.Ok(new AccountIdentity("user@example.com", "Test Org", "pro"));
    private Result<string> _startLoginResult =
        Result<string>.Ok("fake-login-container");
    private IReadOnlyList<string> _runningTransientContainerIds = [];
    private IReadOnlyList<string> _exitedTransientContainerIds = [];
    private Exception? _listTransientException;

    // When true, StreamLogsAsync yields _logLines then blocks until cancellation (does not close).
    private bool _blockAfterLines;

    // Gate for DeliverLoginCodeAsync — when set, Deliver blocks until the TCS is completed.
    private TaskCompletionSource? _deliverGate;

    public int DeliverLoginCodeCallCount { get; private set; }
    public int StopContainerCallCount { get; private set; }
    public int RemoveContainerCallCount { get; private set; }
    public int GetCredentialVolumeAuthStatusCallCount { get; private set; }
    public string? LastDeliveredCode { get; private set; }

    /// <summary>Scripts <see cref="GetCredentialVolumeAuthStatusAsync"/> to return a failure result.</summary>
    public FakeCredentialsOrchestrator WithAuthStatusFailure(Error error)
    {
        _authStatusResult = Result<AccountIdentity>.Fail(error);
        return this;
    }

    /// <summary>Scripts <see cref="GetCredentialVolumeAuthStatusAsync"/> to return a specific identity.</summary>
    public FakeCredentialsOrchestrator WithAuthStatusIdentity(AccountIdentity identity)
    {
        _authStatusResult = Result<AccountIdentity>.Ok(identity);
        return this;
    }

    /// <summary>Scripts <see cref="StartLoginContainerAsync"/> to return a failure.</summary>
    public FakeCredentialsOrchestrator WithStartLoginFailure(Error error)
    {
        _startLoginResult = Result<string>.Fail(error);
        return this;
    }

    /// <summary>
    /// Scripts the container as exited. When <paramref name="exitCode"/> is non-zero,
    /// configures <see cref="GetCredentialVolumeAuthStatusAsync"/> to return a failure result
    /// (simulating a container that exited with an error before writing credentials).
    /// </summary>
    public FakeCredentialsOrchestrator WithExitedContainer(int exitCode = 0)
    {
        if (exitCode != 0)
        {
            _authStatusResult = Result<AccountIdentity>.Fail(
                new Error("Login.ContainerExitedWithError", $"Container exited with code {exitCode}"));
        }

        return this;
    }

    /// <summary>Scripts <see cref="ListExitedTransientContainersAsync"/> and <see cref="ListTransientContainersAsync"/>
    /// to include the given exited (orphaned) container IDs.</summary>
    public FakeCredentialsOrchestrator WithOrphanedTransientContainers(params string[] containerIds)
    {
        _exitedTransientContainerIds = [.. containerIds];
        return this;
    }

    /// <summary>Scripts <see cref="ListTransientContainersAsync"/> to include the given running container IDs.
    /// These are excluded from <see cref="ListExitedTransientContainersAsync"/> so the periodic reaper
    /// (which calls the exited-only method) never sees them.</summary>
    public FakeCredentialsOrchestrator WithRunningTransientContainers(params string[] containerIds)
    {
        _runningTransientContainerIds = [.. containerIds];
        return this;
    }

    /// <summary>Scripts <see cref="ListTransientContainersAsync"/> to throw the given exception.</summary>
    public FakeCredentialsOrchestrator WithListTransientThrows(Exception exception)
    {
        _listTransientException = exception;
        return this;
    }

    /// <summary>
    /// Configures <see cref="StreamLogsAsync"/> to yield <paramref name="logLines"/> and then
    /// block indefinitely until the cancellation token fires (simulating a CLI that re-prompts
    /// after an invalid code without exiting).
    /// </summary>
    public FakeCredentialsOrchestrator WithBlockingStreamAfterLines()
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
    public FakeCredentialsOrchestrator WithBlockingDeliver()
    {
        _deliverGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return this;
    }

    /// <summary>
    /// Releases the gate armed by <see cref="WithBlockingDeliver"/>, allowing
    /// <see cref="DeliverLoginCodeAsync"/> to return.
    /// </summary>
    public void ReleaseDeliver() => _deliverGate?.TrySetResult();

    public Task<Result<string>> StartLoginContainerAsync(
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

    public Task<Result<AccountIdentity>> GetCredentialVolumeAuthStatusAsync(CancellationToken cancellationToken)
    {
        GetCredentialVolumeAuthStatusCallCount++;
        return Task.FromResult(_authStatusResult);
    }

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

    public Task<IReadOnlyList<string>> ListTransientContainersAsync(CancellationToken cancellationToken)
    {
        if (_listTransientException is not null)
        {
            return Task.FromException<IReadOnlyList<string>>(_listTransientException);
        }

        IReadOnlyList<string> all = [.. _runningTransientContainerIds, .. _exitedTransientContainerIds];
        return Task.FromResult(all);
    }

    public Task<IReadOnlyList<string>> ListExitedTransientContainersAsync(CancellationToken cancellationToken)
    {
        if (_listTransientException is not null)
        {
            return Task.FromException<IReadOnlyList<string>>(_listTransientException);
        }

        return Task.FromResult(_exitedTransientContainerIds);
    }

    public Task SeedOnboardingAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
