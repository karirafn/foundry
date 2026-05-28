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

public sealed class TimeoutDetection : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public TimeoutDetection()
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

    private ActiveRun SeedActiveRun(string containerId = "container-timeout-test")
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
        TimeoutStubWorkerOrchestrator orchestrator,
        int timeoutMinutes = 120)
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
            ReportsPath = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}"),
            ApiKey = "test-api-key",
            TimeoutMinutes = timeoutMinutes,
        };

        return new WorkerDispatchService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<WorkerDispatchService>.Instance);
    }

    [Fact]
    public async Task WhenRunHasExceededTimeout_StopsContainerAndTransitionsToFailedRun()
    {
        // Arrange — use TimeoutMinutes = 0 so any run started in the past is immediately timed out
        ActiveRun activeRun = SeedActiveRun();
        TimeoutStubWorkerOrchestrator orchestrator = new(isRunning: true);
        WorkerDispatchService sut = BuildService(orchestrator, timeoutMinutes: 0);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — run transitioned to FailedRun with TimedOut reason
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.Reason.ShouldBeOfType<FailureReason.TimedOut>();
    }

    [Fact]
    public async Task WhenRunHasExceededTimeout_CallsStopOnOrchestrator()
    {
        // Arrange — use TimeoutMinutes = 0 so any run started in the past is immediately timed out
        ActiveRun activeRun = SeedActiveRun();
        TimeoutStubWorkerOrchestrator orchestrator = new(isRunning: true);
        WorkerDispatchService sut = BuildService(orchestrator, timeoutMinutes: 0);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — orchestrator.StopAsync was called with the container ID
        orchestrator.StoppedContainerId.ShouldBe("container-timeout-test");
    }

    [Fact]
    public async Task WhenRunHasNotExceededTimeout_DoesNotTransitionRun()
    {
        // Arrange — use a very large timeout so no run will time out
        ActiveRun activeRun = SeedActiveRun();
        TimeoutStubWorkerOrchestrator orchestrator = new(isRunning: true);
        WorkerDispatchService sut = BuildService(orchestrator, timeoutMinutes: 99999);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — run remains active
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<ActiveRun>();
    }

    internal sealed class TimeoutStubWorkerOrchestrator(bool isRunning) : IWorkerOrchestrator
    {
        public string? StoppedContainerId { get; private set; }

        public Task<Result<string>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Fail(new Error("Test.NoDispatch", "No dispatch in timeout tests")));

        public Task StopAsync(string containerId, CancellationToken cancellationToken)
        {
            StoppedContainerId = containerId;
            return Task.CompletedTask;
        }

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatus?>(new WorkerStatus(IsRunning: isRunning, ExitCode: null, FinishedAt: null));

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
