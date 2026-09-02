using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.WebApi.Persistence;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.StaleStartingRunServiceTests;

public sealed class TickAsync : StaleStartingRunServiceTestBase
{
    [Fact]
    public async Task WhenStaleStartingRunHasNoContainer_FailsTheRun()
    {
        // Arrange
        StartingRun starting = await SeedStaleStartingRunAsync(StaleStartingRunService.StaleStartingRunThreshold + TimeSpan.FromMinutes(1));
        OrchestratorStub orchestrator = new(containers: []);
        StaleStartingRunService sut = BuildService(orchestrator);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        WorkerRun? run = await db.Set<WorkerRun>().FindAsync([starting.Id], TestContext.Current.CancellationToken);
        run.ShouldBeOfType<FailedRun>();
    }

    [Fact]
    public async Task WhenStaleStartingRunHasNoContainer_FailedRunHasContainerErrorReason()
    {
        // Arrange
        StartingRun starting = await SeedStaleStartingRunAsync(StaleStartingRunService.StaleStartingRunThreshold + TimeSpan.FromMinutes(1));
        OrchestratorStub orchestrator = new(containers: []);
        StaleStartingRunService sut = BuildService(orchestrator);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        FailedRun failedRun = db.Set<WorkerRun>()
            .OfType<FailedRun>()
            .Single(r => r.Id == starting.Id);
        failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
    }

    [Fact]
    public async Task WhenStaleStartingRunHasNoContainer_PublishesWorkerRunFailedEvent()
    {
        // Arrange
        StartingRun starting = await SeedStaleStartingRunAsync(StaleStartingRunService.StaleStartingRunThreshold + TimeSpan.FromMinutes(1));
        OrchestratorStub orchestrator = new(containers: []);
        CapturingIntegrationEventDispatcher eventDispatcher = new();
        StaleStartingRunService sut = BuildService(orchestrator, integrationEventDispatcher: eventDispatcher);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        eventDispatcher.Captured
            .OfType<Foundry.Modules.Workers.Contracts.WorkerRunFailed>()
            .ShouldContain(e => e.WorkerRunId == starting.Id);
    }

    [Fact]
    public async Task WhenStaleStartingRunHasMatchingContainer_StopsAndRemovesContainerThenFailsRun()
    {
        // Arrange
        StartingRun starting = await SeedStaleStartingRunAsync(StaleStartingRunService.StaleStartingRunThreshold + TimeSpan.FromMinutes(1));
        ContainerId containerId = ContainerId.From("stale-container-abc");
        IReadOnlyList<(ContainerId, WorkerRunId)> containers = [(containerId, starting.Id)];
        OrchestratorStub orchestrator = new(containers: containers);
        StaleStartingRunService sut = BuildService(orchestrator);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        WorkerRun? run = await db.Set<WorkerRun>().FindAsync([starting.Id], TestContext.Current.CancellationToken);
        run.ShouldSatisfyAllConditions(
            () => run.ShouldBeOfType<FailedRun>(),
            () => orchestrator.StopAndRemoveCallCount.ShouldBe(1),
            () => orchestrator.StoppedAndRemovedContainerIds.ShouldContain(containerId.Value));
    }

    [Fact]
    public async Task WhenFreshStartingRunExists_RunIsNotFailed()
    {
        // Arrange — freshly-created run is well within the threshold
        StartingRun starting = await SeedStartingRunAsync();
        OrchestratorStub orchestrator = new(containers: []);
        StaleStartingRunService sut = BuildService(orchestrator);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        WorkerRun? run = await db.Set<WorkerRun>().FindAsync([starting.Id], TestContext.Current.CancellationToken);
        run.ShouldBeOfType<StartingRun>();
    }

    [Fact]
    public async Task WhenListByLabelAsyncThrowsConnectivityException_NoRunIsFailed()
    {
        // Arrange
        StartingRun starting = await SeedStaleStartingRunAsync(StaleStartingRunService.StaleStartingRunThreshold + TimeSpan.FromMinutes(1));
        OrchestratorStub orchestrator = OrchestratorStub.WithConnectivityException();
        StaleStartingRunService sut = BuildService(orchestrator);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        WorkerRun? run = await db.Set<WorkerRun>().FindAsync([starting.Id], TestContext.Current.CancellationToken);
        run.ShouldBeOfType<StartingRun>();
    }

    [Fact]
    public async Task WhenListByLabelAsyncThrowsConnectivityException_NoContainerIsStopped()
    {
        // Arrange
        StartingRun starting = await SeedStaleStartingRunAsync(StaleStartingRunService.StaleStartingRunThreshold + TimeSpan.FromMinutes(1));
        OrchestratorStub orchestrator = OrchestratorStub.WithConnectivityException();
        StaleStartingRunService sut = BuildService(orchestrator);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.StopAndRemoveCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenContainerRunIdIsOutsideSlotOccupancy_ContainerIsStopped()
    {
        // Arrange — an active + starting run both in DB; a container labelled with an unknown run id
        await SeedStartingRunAsync();
        WorkerRunId unknownRunId = WorkerRunId.New();
        ContainerId orphanContainerId = ContainerId.From("orphan-container-xyz");
        IReadOnlyList<(ContainerId, WorkerRunId)> containers = [(orphanContainerId, unknownRunId)];
        OrchestratorStub orchestrator = new(containers: containers);
        StaleStartingRunService sut = BuildService(orchestrator);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.ShouldSatisfyAllConditions(
            () => orchestrator.StopAndRemoveCallCount.ShouldBe(1),
            () => orchestrator.StoppedAndRemovedContainerIds.ShouldContain(orphanContainerId.Value));
    }
}
