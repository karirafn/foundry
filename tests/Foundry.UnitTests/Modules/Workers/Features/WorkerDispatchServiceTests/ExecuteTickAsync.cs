using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
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

public sealed class ExecuteTickAsync : WorkerDispatchServiceTestBase
{
    private WorkerDispatchService BuildService(
        CapturingIntegrationEventDispatcher dispatcher,
        int maxConcurrent = 3)
    {
        WorkerOptions workerOptions = new()
        {
            Image = "test-image:latest",
        };

        StubGlobalSettingsQueries settingsQueries = new(maxConcurrent: maxConcurrent, timeoutMinutes: 120);

        return base.BuildService(
            new NullWorkerOrchestrator(),
            workerOptions,
            dispatcher,
            settingsQueries: settingsQueries);
    }

    [Fact]
    public async Task WhenCapacityAvailable_DispatchesWorkerCapacityAvailable()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldContain(e => e is WorkerCapacityAvailable);
    }

    [Fact]
    public async Task WhenCapacityAvailable_DispatchedEventCarriesNonEmptyWorkerRunId()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerCapacityAvailable @event = dispatcher.Captured
            .OfType<WorkerCapacityAvailable>()
            .ShouldHaveSingleItem();
        @event.WorkerRunId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task WhenMaxConcurrentReached_DoesNotDispatchWorkerCapacityAvailable()
    {
        // Arrange — seed MaxConcurrent ActiveRun records
        await using (FoundryDbContext db = CreateDbContext())
        {
            IssueId issueId = IssueId.New();
            StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
            ActiveRun activeRun = starting.Activate(ContainerId.From("container-existing"), BranchName.From("feat/1-default"), MonitoredRepositoryId.New());
            db.Set<WorkerRun>().Add(activeRun);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        CapturingIntegrationEventDispatcher dispatcher = new();
        // MaxConcurrent = 1, already have 1 active run
        WorkerDispatchService sut = BuildService(dispatcher, maxConcurrent: 1);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — no WorkerCapacityAvailable dispatched
        dispatcher.Captured.ShouldNotContain(e => e is WorkerCapacityAvailable);
    }

    [Fact]
    public async Task WhenCapacityAvailable_CompletesWithoutError()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(dispatcher);

        // Act
        Exception? exception = await Record.ExceptionAsync(
            () => sut.ExecuteTickAsync(TestContext.Current.CancellationToken));

        // Assert
        exception.ShouldBeNull();
    }

    [Fact]
    public async Task WhenBelowMaxConcurrent_DispatchesExactlyOneEvent()
    {
        // Arrange — seed one active run, max is 3
        await using (FoundryDbContext db = CreateDbContext())
        {
            IssueId issueId = IssueId.New();
            StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
            ActiveRun activeRun = starting.Activate(ContainerId.From("container-existing"), BranchName.From("feat/1-default"), MonitoredRepositoryId.New());
            db.Set<WorkerRun>().Add(activeRun);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(dispatcher, maxConcurrent: 3);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured
            .OfType<WorkerCapacityAvailable>()
            .ShouldHaveSingleItem();
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
}
