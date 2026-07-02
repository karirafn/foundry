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

    public int DeliverLoginCodeCallCount { get; private set; }
    public int StopContainerCallCount { get; private set; }
    public int RemoveContainerCallCount { get; private set; }
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

    public Task DeliverLoginCodeAsync(string containerId, string code, CancellationToken cancellationToken)
    {
        DeliverLoginCodeCallCount++;
        LastDeliveredCode = code;
        return Task.CompletedTask;
    }

    public Task<Result<AccountIdentity>> GetAuthStatusAsync(string containerId, CancellationToken cancellationToken)
        => Task.FromResult(_authStatusResult);

    public Task<IReadOnlyList<ContainerId>> ListLoginContainersByLabelAsync(CancellationToken cancellationToken)
        => Task.FromResult(_loginContainerIds);

    public Task SeedOnboardingAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
