using System.Runtime.CompilerServices;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.StaleStartingRunServiceTests;

/// <summary>
/// Tests for orphaned data-root volume reaping wired into <see cref="StaleStartingRunService.TickAsync"/>.
/// </summary>
public sealed class OrphanedDataRootVolumeReaping : StaleStartingRunServiceTestBase
{
    [Fact]
    public async Task WhenTickRuns_ReapsOrphanedDataRootVolumesWithSlotOccupancySet()
    {
        // Arrange — seed one starting and one active run so slotRunIds is non-empty
        StartingRun starting = await SeedStartingRunAsync();
        ActiveRun active = await SeedActiveRunAsync("active-container-abc");
        OrchestratorStub orchestrator = new(containers: []);
        StaleStartingRunService sut = BuildService(orchestrator);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        HashSet<WorkerRunId> expectedIds = [starting.Id, active.Id];
        orchestrator.ShouldSatisfyAllConditions(
            () => orchestrator.ReapOrphanedDataRootVolumesCallCount.ShouldBe(1),
            () => orchestrator.LastReapActiveRunIds.ShouldNotBeNull(),
            () => orchestrator.LastReapActiveRunIds!.ShouldBeSubsetOf(expectedIds),
            () => expectedIds.ShouldBeSubsetOf(orchestrator.LastReapActiveRunIds!));
    }

    [Fact]
    public async Task WhenDaemonUnreachableDuringContainerScan_ReapIsNotCalled()
    {
        // Arrange — daemon unreachable; tick should defer entirely before reaching the reap call
        OrchestratorStub orchestrator = OrchestratorStub.WithConnectivityException();
        StaleStartingRunService sut = BuildService(orchestrator);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.ReapOrphanedDataRootVolumesCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenReapOrphanedDataRootVolumesThrows_StaleRunIsStillFailed()
    {
        // Arrange — a stale starting run exists; orchestrator reap throws but stale-run loop must continue
        StartingRun stale = await SeedStaleStartingRunAsync(
            StaleStartingRunService.StaleStartingRunThreshold + TimeSpan.FromMinutes(1));
        ThrowingReapOrchestratorStub orchestrator = new();
        StaleStartingRunService sut = BuildService(orchestrator);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        WorkerRun? run = await db.Set<WorkerRun>()
            .FindAsync([stale.Id], TestContext.Current.CancellationToken);
        run.ShouldBeOfType<FailedRun>();
    }

    /// <summary>
    /// An <see cref="IWorkerOrchestrator"/> implementation whose
    /// <see cref="ReapOrphanedDataRootVolumesAsync"/> always throws so tests can verify that a reap
    /// failure does not abort the stale-run failing loop. All other methods are no-ops matching the
    /// <see cref="OrchestratorStub"/> behaviour.
    /// </summary>
    private sealed class ThrowingReapOrchestratorStub : IWorkerOrchestrator
    {
        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test", "Not supported")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.NotFound());

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ReapOrphanedDataRootVolumesAsync(
            IReadOnlySet<WorkerRunId> activeRunIds,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("Simulated reap failure");
    }
}
