using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.WebApi.Persistence;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.StaleStartingRunServiceTests;

/// <summary>
/// Tests for the orphaned-container reaping responsibility of <see cref="StaleStartingRunService"/>:
/// containers whose run id falls outside slot occupancy (starting ∪ active) are stopped and removed.
/// These cases migrated from <c>WorkerDispatchServiceTests/OrphanedContainerCleanup.cs</c> when the
/// reaping responsibility moved from <c>WorkerDispatchService.RemoveUnknownContainersAsync</c> to
/// <see cref="StaleStartingRunService"/>.
/// </summary>
public sealed class OrphanedContainerReaping : StaleStartingRunServiceTestBase
{
    [Fact]
    public async Task WhenDockerReturnsContainerMatchingCompletedRun_StopsContainer()
    {
        // Arrange — a completed run's id is outside slot occupancy; its container must be reaped
        ContainerId containerId = ContainerId.From("completed-container");
        WorkerRunId completedRunId = WorkerRunId.New();
        IReadOnlyList<(ContainerId, WorkerRunId)> containers = [(containerId, completedRunId)];
        OrchestratorStub orchestrator = new(containers: containers);
        StaleStartingRunService sut = BuildService(orchestrator);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.ShouldSatisfyAllConditions(
            () => orchestrator.StopAndRemoveCallCount.ShouldBe(1),
            () => orchestrator.StoppedAndRemovedContainerIds.ShouldContain(containerId.Value));
    }

    [Fact]
    public async Task WhenDockerReturnsContainerMatchingActiveRun_DoesNotStopContainer()
    {
        // Arrange — an active run's id is inside slot occupancy; its container must not be reaped
        ActiveRun active = await SeedActiveRunAsync("active-container-xyz");
        IReadOnlyList<(ContainerId, WorkerRunId)> containers = [(active.ContainerId, active.Id)];
        OrchestratorStub orchestrator = new(containers: containers);
        StaleStartingRunService sut = BuildService(orchestrator);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.StopAndRemoveCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenDockerScanThrowsNonConnectivityException_DefersTick()
    {
        // Arrange — ListByLabelAsync throws a non-connectivity exception (e.g. daemon unavailable);
        // the whole tick exits early (nothing to iterate over), deferring all work to the next tick.
        StartingRun stale = await SeedStaleStartingRunAsync(StaleStartingRunService.StaleStartingRunThreshold + TimeSpan.FromMinutes(1));
        OrchestratorStub orchestrator = new(listThrows: true);
        StaleStartingRunService sut = BuildService(orchestrator);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — the stale run remains in starting state (tick deferred entirely)
        await using FoundryDbContext db = CreateDbContext();
        WorkerRun? run = await db.Set<WorkerRun>()
            .FindAsync([stale.Id], TestContext.Current.CancellationToken);
        run.ShouldBeOfType<StartingRun>();
    }
}
