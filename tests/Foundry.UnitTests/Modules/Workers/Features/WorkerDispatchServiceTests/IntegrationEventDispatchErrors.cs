using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Login;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class IntegrationEventDispatchErrors : WorkerDispatchServiceTestBase
{
    [Fact]
    public async Task WhenDispatchThrowsOnContainerCompletion_TickCompletesAndRunIsStillTransitioned()
    {
        // Arrange
        SeedActiveRun("exited-dispatch-error");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        StubWorkerOrchestrator orchestrator = new(status: exitedStatus);
        ThrowingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(orchestrator, integrationEventDispatcher: dispatcher);

        // Act — must not throw even though dispatcher throws
        Task act = sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        await Should.NotThrowAsync(act);

        // Assert — WorkerRun still transitioned to CompletedRun
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<CompletedRun>();
    }

    [Fact]
    public async Task WhenDispatchThrowsOnContainerFailure_TickCompletesAndRunIsStillTransitioned()
    {
        // Arrange
        SeedActiveRun("failed-dispatch-error");
        WorkerStatus failedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        StubWorkerOrchestrator orchestrator = new(status: failedStatus);
        ThrowingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(orchestrator, integrationEventDispatcher: dispatcher);

        // Act — must not throw even though dispatcher throws
        Task act = sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        await Should.NotThrowAsync(act);

        // Assert — WorkerRun still transitioned to FailedRun
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<FailedRun>();
    }

    [Fact]
    public async Task WhenDispatchThrowsOnOrphanedContainer_TickCompletesAndRunIsStillTransitioned()
    {
        // Arrange — seed before first tick so reconciliation processes it
        SeedActiveRun("orphaned-dispatch-error");
        StubWorkerOrchestrator orchestrator = new(status: null);
        ThrowingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(orchestrator, integrationEventDispatcher: dispatcher);

        // Act — must not throw even though dispatcher throws
        Task act = sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        await Should.NotThrowAsync(act);

        // Assert — WorkerRun still transitioned to FailedRun
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<FailedRun>();
    }

    [Fact]
    public async Task WhenDispatchThrowsOnCapacityAvailable_TickCompletesWithoutPropagating()
    {
        // Arrange — no active runs so the capacity-available path is reached
        ThrowingAllIntegrationEventDispatcher dispatcher = new();
        StubWorkerOrchestrator orchestrator = new(status: null);
        WorkerDispatchService sut = BuildService(orchestrator, integrationEventDispatcher: dispatcher);

        // Act — must not throw even though the capacity-available dispatch throws
        Task act = sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        await Should.NotThrowAsync(act);
    }

    internal sealed class StubWorkerOrchestrator(WorkerStatus? status) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in error tests")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult(status);

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

    // Throws for run-specific events (WorkerRunCompleted/WorkerRunFailed) but not for
    // WorkerCapacityAvailable, which is dispatched via TryDispatchAsync at the end of each tick.
    internal sealed class ThrowingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            IIntegrationEvent[] eventArray = events.ToArray();

            if (eventArray.Any(e => e is WorkerCapacityAvailable))
            {
                return Task.CompletedTask;
            }

            return Task.FromException(new InvalidOperationException("Simulated dispatch failure"));
        }
    }

    // Throws for every event type — used to verify the capacity-available path is guarded.
    internal sealed class ThrowingAllIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
            => Task.FromException(new InvalidOperationException("Simulated dispatch failure"));
    }
}
