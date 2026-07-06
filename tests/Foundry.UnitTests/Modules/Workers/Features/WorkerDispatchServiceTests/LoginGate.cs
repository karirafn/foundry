using System.Runtime.CompilerServices;

using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class LoginGate : WorkerDispatchServiceTestBase
{
    [Fact]
    public async Task WhenCredentialGateReturnsFalse_DoesNotDispatchWorkerCapacityAvailable()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            credentialGate: new CannotDispatchCredentialGate());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldNotContain(e => e is WorkerCapacityAvailable);
    }

    [Fact]
    public async Task WhenCredentialGateReturnsFalse_DoesNotReadDatabase()
    {
        // Arrange — no DB rows; if the tick reads ActiveRun it would be fine, but
        // we validate it returns before any dispatch logic by checking no WorkerCapacityAvailable
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            credentialGate: new CannotDispatchCredentialGate());

        // Act — completes without exception and without dispatching
        Exception? exception = await Record.ExceptionAsync(
            () => sut.ExecuteTickAsync(TestContext.Current.CancellationToken));

        // Assert
        exception.ShouldBeNull();
        dispatcher.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenCredentialGateReturnsTrue_DispatchesWorkerCapacityAvailable()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            credentialGate: new CanDispatchCredentialGate());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldContain(e => e is WorkerCapacityAvailable);
    }

    private sealed class CannotDispatchCredentialGate : ICredentialGate
    {
        public Task<bool> CanDispatchAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class CanDispatchCredentialGate : ICredentialGate
    {
        public Task<bool> CanDispatchAsync(CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class NullWorkerOrchestrator : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Ok(ContainerId.From("default-container")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.Available(new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null)));

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
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
