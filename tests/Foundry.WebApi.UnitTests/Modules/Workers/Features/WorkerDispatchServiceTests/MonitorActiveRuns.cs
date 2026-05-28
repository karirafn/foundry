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

public sealed class MonitorActiveRuns : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public MonitorActiveRuns()
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

    private ActiveRun SeedActiveRun(string containerId = "container-123")
    {
        using FoundryDbContext db = CreateDbContext();
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun activeRun = starting.Activate(containerId);
        db.WorkerRuns.Add(activeRun);
        db.SaveChanges();
        return activeRun;
    }

    private WorkerDispatchService BuildService(
        MonitoringStubWorkerOrchestrator orchestrator,
        WorkerOptions? workerOptions = null)
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

        WorkerOptions options = workerOptions ?? new WorkerOptions
        {
            Image = "test-image:latest",
            MaxConcurrent = 3,
            ConfigPath = "/tmp/config",
            ReportsPath = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}"),
            ApiKey = "test-api-key",
            TimeoutMinutes = 120,
        };

        return new WorkerDispatchService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<WorkerDispatchService>.Instance);
    }

    [Fact]
    public async Task WhenContainerDisappearsAfterReconciliation_TransitionsToFailedRunWithContainerError()
    {
        // Arrange — seed the run after the first tick so reconciliation does not process it;
        // on the second tick the monitoring loop finds the container missing
        MonitoringStubWorkerOrchestrator orchestrator = new(status: null);
        WorkerDispatchService sut = BuildService(orchestrator);

        // First tick: no active runs exist yet, so reconciliation has nothing to process
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Seed the active run after reconciliation has already completed
        SeedActiveRun("missing-container");

        // Act — second tick: reconciliation skipped; monitoring sees null status
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.ContainerError error = failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
        error.Message.ShouldBe("Container not found");
    }

    [Fact]
    public async Task WhenContainerExitsWithZero_TransitionsToCompletedRun()
    {
        // Arrange
        ActiveRun activeRun = SeedActiveRun("exited-container");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        MonitoringStubWorkerOrchestrator orchestrator = new(status: exitedStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        CompletedRun completedRun = run.ShouldBeOfType<CompletedRun>();
        completedRun.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task WhenContainerExitsWithNonZero_TransitionsToFailedRunWithNonZeroExit()
    {
        // Arrange
        ActiveRun activeRun = SeedActiveRun("failed-container");
        WorkerStatus failedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        MonitoringStubWorkerOrchestrator orchestrator = new(status: failedStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.NonZeroExit nonZeroExit = failedRun.Reason.ShouldBeOfType<FailureReason.NonZeroExit>();
        nonZeroExit.ExitCode.ShouldBe(1);
    }

    [Fact]
    public async Task WhenContainerStillRunning_RunRemainsActive()
    {
        // Arrange
        ActiveRun activeRun = SeedActiveRun("running-container");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        MonitoringStubWorkerOrchestrator orchestrator = new(status: runningStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<ActiveRun>();
    }

    internal sealed class MonitoringStubWorkerOrchestrator(WorkerStatus? status) : IWorkerOrchestrator
    {
        public string? LastStoppedContainerId { get; private set; }

        public Task<Result<string>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Fail(new Error("Test.NoDispatch", "No dispatch in monitor tests")));

        public Task StopAsync(string containerId, CancellationToken cancellationToken)
        {
            LastStoppedContainerId = containerId;
            return Task.CompletedTask;
        }

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

        public Task<ClaimedIssueDispatch?> ClaimNextQueuedIssueAsync(Guid workerRunId, CancellationToken cancellationToken)
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
