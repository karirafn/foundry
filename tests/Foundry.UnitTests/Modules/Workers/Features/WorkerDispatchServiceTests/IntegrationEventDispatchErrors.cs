using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
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

    internal sealed class StubWorkerOrchestrator(WorkerStatus? status) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in error tests")));

        public Task StopAsync(string containerId, CancellationToken cancellationToken)
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
    }

    // Throws for run-specific events (WorkerRunCompleted/WorkerRunFailed) but not for
    // WorkerCapacityAvailable, which is dispatched unconditionally at the end of each tick.
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
}
