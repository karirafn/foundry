using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

public sealed class ReservesSlot : WorkerDispatchServiceTestBase
{
    private WorkerDispatchService BuildService(int maxConcurrent = 3)
    {
        StubGlobalSettingsQueries settingsQueries = new(maxConcurrent: maxConcurrent, timeoutMinutes: 120);

        return base.BuildService(
            new NullWorkerOrchestrator(),
            settingsQueries: settingsQueries);
    }

    [Fact]
    public async Task WhenCapacityAvailable_PersistsExactlyOneReservation()
    {
        // Arrange
        WorkerDispatchService sut = BuildService(maxConcurrent: 3);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        using FoundryDbContext db = CreateDbContext();
        List<DispatchReservation> reservations = await db.Set<DispatchReservation>()
            .ToListAsync(TestContext.Current.CancellationToken);
        reservations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenCapacityAvailable_ReservationIdMatchesDispatchedWorkerRunId()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        StubGlobalSettingsQueries settingsQueries = new(maxConcurrent: 3, timeoutMinutes: 120);
        WorkerDispatchService sut = base.BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: settingsQueries);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerCapacityAvailable dispatched = dispatcher.Captured
            .ShouldHaveSingleItem()
            .ShouldBeOfType<WorkerCapacityAvailable>();

        using FoundryDbContext db = CreateDbContext();
        DispatchReservation reservation = await db.Set<DispatchReservation>()
            .SingleAsync(TestContext.Current.CancellationToken);
        reservation.Id.ShouldBe(dispatched.WorkerRunId);
    }

    [Fact]
    public async Task WhenReservationPresentAndMaxConcurrentOne_DoesNotCreateSecondReservation()
    {
        // Arrange
        WorkerRunId existingRunId = WorkerRunId.New();
        DispatchReservation existing = new DispatchReservationBuilder()
            .WithWorkerRunId(existingRunId)
            .Build();

        using (FoundryDbContext seedDb = CreateDbContext())
        {
            seedDb.Set<DispatchReservation>().Add(existing);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WorkerDispatchService sut = BuildService(maxConcurrent: 1);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        using FoundryDbContext db = CreateDbContext();
        List<DispatchReservation> reservations = await db.Set<DispatchReservation>()
            .ToListAsync(TestContext.Current.CancellationToken);
        reservations.Count.ShouldBe(1);
        reservations[0].Id.ShouldBe(existingRunId);
    }

    [Fact]
    public async Task WhenReservationPresentAndMaxConcurrentOne_DoesNotDispatchWorkerCapacityAvailable()
    {
        // Arrange
        DispatchReservation existing = new DispatchReservationBuilder().Build();

        using (FoundryDbContext seedDb = CreateDbContext())
        {
            seedDb.Set<DispatchReservation>().Add(existing);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        CapturingIntegrationEventDispatcher dispatcher = new();
        StubGlobalSettingsQueries settingsQueries = new(maxConcurrent: 1, timeoutMinutes: 120);
        WorkerDispatchService sut = base.BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: settingsQueries);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldNotContain(e => e is WorkerCapacityAvailable);
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
            => Task.FromResult<WorkerStatusProbe>(
                new WorkerStatusProbe.Available(new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null)));

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
}
