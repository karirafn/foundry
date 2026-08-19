using System.Runtime.CompilerServices;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain.ValueObjects;
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

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

public sealed class CapacityGate : WorkerDispatchServiceTestBase
{
    private WorkerDispatchService BuildService(
        CapturingIntegrationEventDispatcher dispatcher,
        int maxConcurrent)
    {
        StubGlobalSettingsQueries settingsQueries = new(maxConcurrent: maxConcurrent, timeoutMinutes: 120);

        return base.BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: settingsQueries);
    }

    private void SeedStartingRun()
    {
        using FoundryDbContext db = CreateDbContext();
        IssueId issueId = IssueId.New();
        StartingRun startingRun = StartingRun.Begin(issueId, WorkerRunId.New());
        db.Set<WorkerRun>().Add(startingRun);
        db.SaveChanges();
    }

    [Fact]
    public async Task WhenStartingRunPresentAndMaxConcurrentOne_DoesNotDispatchWorkerCapacityAvailable()
    {
        // Arrange
        SeedStartingRun();
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(dispatcher, maxConcurrent: 1);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldNotContain(e => e is WorkerCapacityAvailable);
    }

    [Fact]
    public async Task WhenStartingAndActiveRunsTotalEqualsMaxConcurrent_DoesNotDispatchWorkerCapacityAvailable()
    {
        // Arrange — one StartingRun + one ActiveRun = 2 slots used; MaxConcurrent = 2
        SeedStartingRun();
        SeedActiveRun("container-active-123");
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(dispatcher, maxConcurrent: 2);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldNotContain(e => e is WorkerCapacityAvailable);
    }

    [Fact]
    public async Task WhenOnlyStartingRunsAndCapacityAvailable_DispatchesWorkerCapacityAvailable()
    {
        // Arrange — one StartingRun; MaxConcurrent = 2 means one slot remains
        SeedStartingRun();
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(dispatcher, maxConcurrent: 2);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldContain(e => e is WorkerCapacityAvailable);
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
