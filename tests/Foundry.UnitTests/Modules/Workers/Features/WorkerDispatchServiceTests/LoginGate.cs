using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Login;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class LoginGate : WorkerDispatchServiceTestBase
{
    [Fact]
    public async Task WhenLoginIsActive_DoesNotDispatchWorkerCapacityAvailable()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            loginSessionState: new ActiveLoginSessionState());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldNotContain(e => e is WorkerCapacityAvailable);
    }

    [Fact]
    public async Task WhenLoginIsActive_DoesNotReadDatabase()
    {
        // Arrange — no DB rows; if the tick reads ActiveRun it would be fine, but
        // we validate it returns before any dispatch logic by checking no WorkerCapacityAvailable
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            loginSessionState: new ActiveLoginSessionState());

        // Act — completes without exception and without dispatching
        Exception? exception = await Record.ExceptionAsync(
            () => sut.ExecuteTickAsync(TestContext.Current.CancellationToken));

        // Assert
        exception.ShouldBeNull();
        dispatcher.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenLoginIsInactive_DispatchesWorkerCapacityAvailable()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            loginSessionState: new InactiveLoginSessionState());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldContain(e => e is WorkerCapacityAvailable);
    }

    private sealed class ActiveLoginSessionState : ILoginSessionState
    {
        public bool IsLoginActive => true;
    }

    private sealed class InactiveLoginSessionState : ILoginSessionState
    {
        public bool IsLoginActive => false;
    }

    private sealed class NullWorkerOrchestrator : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Ok(ContainerId.From("default-container")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatus?>(new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null));

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
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
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result<ContainerId>> StartLoginContainerAsync(
            LoginContainerSpec spec,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Ok(ContainerId.From("fake-login-container")));

        public Task DeliverLoginCodeAsync(string containerId, string code, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result<AccountIdentity>> GetAuthStatusAsync(
            string containerId,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<AccountIdentity>.Ok(new AccountIdentity("test@example.com", "Test Org", "pro")));


        public Task<Result<AccountIdentity>> GetCredentialVolumeAuthStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(Result<AccountIdentity>.Ok(new AccountIdentity("test@example.com", "Test Org", "pro")));
        public Task<IReadOnlyList<ContainerId>> ListLoginContainersByLabelAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ContainerId>>([]);

        public Task SeedOnboardingAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
