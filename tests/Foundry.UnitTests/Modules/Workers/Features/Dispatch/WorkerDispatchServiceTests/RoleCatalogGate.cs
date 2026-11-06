using System.Runtime.CompilerServices;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

public sealed class RoleCatalogGate : WorkerDispatchServiceTestBase
{
    // Literal on purpose: the composed-host smoke test waits on this exact text.
    private const string SkipMessage = "Dispatch skipped: worker role catalog is not loaded.";
    private const string ResumeMessage = "Dispatch resumed: worker role catalog is loaded.";

    [Fact]
    public async Task WhenTheRoleCatalogIsNotLoaded_DoesNotDispatchWorkerCapacityAvailable()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            roleCatalog: StubWorkerRoleCatalogStore.Empty());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldNotContain(e => e is WorkerCapacityAvailable);
    }

    [Fact]
    public async Task WhenTheRoleCatalogIsNotLoaded_WritesNoDispatchReservation()
    {
        // Arrange
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            roleCatalog: StubWorkerRoleCatalogStore.Empty());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        (await db.Set<DispatchReservation>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task WhenTheRoleCatalogIsNotLoaded_LogsThatDispatchWasSkipped()
    {
        // Arrange
        CapturingLogger logger = new();
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            logger: logger,
            roleCatalog: StubWorkerRoleCatalogStore.Empty());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message == SkipMessage);
    }

    [Fact]
    public async Task WhenTheRoleCatalogStaysUnloadedAcrossTicks_LogsTheSkipAtInformationOnce()
    {
        // Arrange
        CapturingLogger logger = new();
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            logger: logger,
            roleCatalog: StubWorkerRoleCatalogStore.Empty());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        logger.Entries
            .Count(e => e.Level == LogLevel.Information && e.Message == SkipMessage)
            .ShouldBe(1);
    }

    [Fact]
    public async Task WhenTheRoleCatalogIsPublishedAfterASkippedTick_LogsTheResumeAtInformationOnce()
    {
        // Arrange
        CapturingLogger logger = new();
        StubWorkerRoleCatalogStore catalogStore = StubWorkerRoleCatalogStore.Empty();
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            logger: logger,
            roleCatalog: catalogStore);
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        catalogStore.Publish(StubWorkerRoleCatalogStore.Catalog());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        logger.Entries
            .Count(e => e.Level == LogLevel.Information && e.Message == ResumeMessage)
            .ShouldBe(1);
    }

    [Fact]
    public async Task WhenTheRoleCatalogIsPublishedAfterAnEarlierSkippedTick_DispatchesWorkerCapacityAvailable()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        StubWorkerRoleCatalogStore catalogStore = StubWorkerRoleCatalogStore.Empty();
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            roleCatalog: catalogStore);
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        dispatcher.Captured.ShouldBeEmpty();
        catalogStore.Publish(StubWorkerRoleCatalogStore.Catalog());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.Count(e => e is WorkerCapacityAvailable).ShouldBe(1);
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

        public Task ReapOrphanedDataRootVolumesAsync(
            IReadOnlySet<WorkerRunId> activeRunIds,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
