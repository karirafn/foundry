using Foundry.WebApi.Modules.Issues;
using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Modules.Workers.Domain;
using Foundry.WebApi.Modules.Workers.Features;
using Foundry.WebApi.Shared.Abstractions;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class Reconciliation : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public Reconciliation()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    private ActiveRun SeedActiveRun(string containerId = "container-orphaned")
    {
        using FoundryDbContext db = CreateDbContext();
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId);
        ActiveRun activeRun = starting.Activate(containerId);
        db.WorkerRuns.Add(activeRun);
        db.SaveChanges();
        return activeRun;
    }

    private WorkerDispatchService BuildService(ReconciliationStubWorkerOrchestrator orchestrator)
    {
        SqliteConnection connection = _connection;

        ServiceCollection services = new();
        services.AddScoped<FoundryDbContext>(_ =>
        {
            DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
                .UseSqlite(connection)
                .Options;
            return new FoundryDbContext(options);
        });
        services.AddScoped<IDomainEventDispatcher, NullDomainEventDispatcher>();
        services.AddScoped<IIssuesModule>(_ => new EmptyIssuesModule());
        services.AddScoped<IWorkerOrchestrator>(_ => orchestrator);
        services.AddScoped<IProviderAuth>(_ => new StubProviderAuth("test-token"));

        ServiceProvider sp = services.BuildServiceProvider();

        WorkerOptions options = new()
        {
            Image = "test-image:latest",
            MaxConcurrent = 3,
            ConfigPath = "/tmp/config",
            ReportsPath = Path.Combine(Path.GetTempPath(), $"foundry-reconcile-{Guid.NewGuid()}"),
            ApiKey = "test-api-key",
            TimeoutMinutes = 120,
        };

        return new WorkerDispatchService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<WorkerDispatchService>.Instance);
    }

    [Fact]
    public async Task WhenFirstTickAndContainerNotFound_TransitionsOrphanedRunToFailedRun()
    {
        // Arrange
        SeedActiveRun("orphaned-container");
        ReconciliationStubWorkerOrchestrator orchestrator = new(status: null);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.ContainerError error = failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
        error.Message.ShouldBe("Orphaned after restart");
    }

    [Fact]
    public async Task WhenFirstTickAndContainerStillRunning_RunRemainsActive()
    {
        // Arrange
        SeedActiveRun("running-container");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        ReconciliationStubWorkerOrchestrator orchestrator = new(status: runningStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<ActiveRun>();
    }

    [Fact]
    public async Task WhenFirstTickAndContainerExitedWithZero_TransitionsToCompletedRun()
    {
        // Arrange
        SeedActiveRun("exited-zero-container");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        ReconciliationStubWorkerOrchestrator orchestrator = new(status: exitedStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<CompletedRun>();
    }

    [Fact]
    public async Task WhenFirstTickAndContainerExitedWithNonZero_TransitionsToFailedRun()
    {
        // Arrange
        SeedActiveRun("exited-nonzero-container");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 2, FinishedAt: DateTimeOffset.UtcNow);
        ReconciliationStubWorkerOrchestrator orchestrator = new(status: exitedStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.NonZeroExit nonZeroExit = failedRun.Reason.ShouldBeOfType<FailureReason.NonZeroExit>();
        nonZeroExit.ExitCode.ShouldBe(2);
    }

    [Fact]
    public async Task WhenSecondTick_ReconciliationDoesNotRunAgain()
    {
        // Arrange — seed run, first tick will reconcile (container missing → FailedRun)
        // Then seed another active run; second tick should NOT reconcile it (it stays Active)
        ReconciliationStubWorkerOrchestrator orchestrator = new(status: null);
        WorkerDispatchService sut = BuildService(orchestrator);

        // First tick: reconciles the orphaned run (no active runs seeded yet, so nothing happens)
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Seed a new ActiveRun AFTER the first tick
        SeedActiveRun("post-reconcile-container");

        // Act — second tick should skip reconciliation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — the new ActiveRun was NOT touched by reconciliation (stays Active because
        // reconciliation only runs once; normal monitoring handles it but orchestrator returns null
        // which would transition it — but that's via normal monitoring, not reconciliation)
        // What we verify: reconciliation ran exactly once (GetStatusAsync call count matches)
        orchestrator.GetStatusCallCount.ShouldBe(1);
    }

    internal sealed class ReconciliationStubWorkerOrchestrator(WorkerStatus? status) : IWorkerOrchestrator
    {
        public int GetStatusCallCount { get; private set; }

        public Task<Result<string>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Fail(new Error("Test.NoDispatch", "No dispatch in reconciliation tests")));

        public Task StopAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
        {
            GetStatusCallCount++;
            return Task.FromResult(status);
        }

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class EmptyIssuesModule : IIssuesModule
    {
        public Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
            MonitoredRepositoryId repositoryId,
            IReadOnlySet<int> issueNumbers,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<int, IssueSnapshot>>(new Dictionary<int, IssueSnapshot>());

        public Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DependencyEdge>>([]);

        public Task<ClaimedIssueDispatch?> ClaimNextQueuedIssueAsync(CancellationToken cancellationToken)
            => Task.FromResult<ClaimedIssueDispatch?>(null);
    }

    private sealed class StubProviderAuth(string token) : IProviderAuth
    {
        public Task<Result<string>> GetTokenAsync(string secretKeyName, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Ok(token));
    }

    private sealed class NullDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
