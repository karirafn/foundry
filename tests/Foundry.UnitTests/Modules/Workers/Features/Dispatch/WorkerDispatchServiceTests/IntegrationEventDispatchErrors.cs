using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

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
        // WorkerRunCompleted is enqueued via TryDispatchAsync (which catches) BEFORE TransitionAsync;
        // the throw is swallowed and the transition succeeds.
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<CompletedRun>();
    }

    [Fact]
    public async Task WhenDispatchThrowsInsideBridgeHandler_TransactionRollsBackAndTickThrows()
    {
        // Arrange — exited with non-zero exit: WorkerRunFailed domain event is raised,
        // WorkerRunFailedBridgeHandler calls DispatchAsync which throws.
        // Because the bridge handler runs inside the TransitionAsync transaction, the throw
        // rolls back the transaction — state change is reverted (run stays ActiveRun) and
        // the tick propagates the exception rather than silently swallowing it.
        SeedActiveRun("failed-dispatch-error");
        WorkerStatus failedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        StubWorkerOrchestrator orchestrator = new(status: failedStatus);
        ThrowingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(orchestrator, integrationEventDispatcher: dispatcher);

        // Act — dispatcher throws inside TransitionAsync → transaction rolls back → tick throws
        Task act = sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        await Should.ThrowAsync<InvalidOperationException>(act);

        // Assert — transaction rolled back; run remains ActiveRun (state change was not committed)
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<ActiveRun>();
    }

    [Fact]
    public async Task WhenDispatchThrowsInsideBridgeHandlerOnOrphanedContainer_TransactionRollsBackAndTickThrows()
    {
        // Arrange — orphaned container (NotFound status): outcome resolver returns Failure,
        // WorkerRunFailed domain event → bridge handler → dispatcher throws.
        SeedActiveRun("orphaned-dispatch-error");
        StubWorkerOrchestrator orchestrator = new(status: null);
        ThrowingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(orchestrator, integrationEventDispatcher: dispatcher);

        // Act — dispatcher throws inside TransitionAsync → transaction rolls back → tick throws
        Task act = sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        await Should.ThrowAsync<InvalidOperationException>(act);

        // Assert — run stays ActiveRun (transaction was rolled back)
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<ActiveRun>();
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

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(status is null
                ? new WorkerStatusProbe.NotFound()
                : new WorkerStatusProbe.Available(status));

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
