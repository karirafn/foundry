using System.Text.Json;

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

public sealed class ReportIngestion : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SqliteConnection _connection;
    private readonly string _reportsBasePath;

    public ReportIngestion()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();

        _reportsBasePath = Path.Combine(Path.GetTempPath(), $"foundry-reports-{Guid.NewGuid()}");
        Directory.CreateDirectory(_reportsBasePath);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (Directory.Exists(_reportsBasePath))
        {
            Directory.Delete(_reportsBasePath, recursive: true);
        }

        await _connection.DisposeAsync();
    }

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    private ActiveRun SeedActiveRun()
    {
        using FoundryDbContext db = CreateDbContext();
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun activeRun = starting.Activate("container-report-test");
        db.WorkerRuns.Add(activeRun);
        db.SaveChanges();
        return activeRun;
    }

    private string WriteReportFile(WorkerRunId workerRunId, int sequenceNumber, object payload)
    {
        string runDir = Path.Combine(_reportsBasePath, workerRunId.Value.ToString());
        Directory.CreateDirectory(runDir);

        string filePath = Path.Combine(runDir, $"report-{sequenceNumber}.json");
        // Synchronous write is acceptable here — setup helper called before async test code.
        File.WriteAllText(filePath, JsonSerializer.Serialize(payload, JsonOptions));
        return filePath;
    }

    private WorkerDispatchService BuildService()
    {
        SqliteConnection connection = _connection;
        string reportsPath = _reportsBasePath;

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
        services.AddScoped<IWorkerOrchestrator>(_ => new RunningStubWorkerOrchestrator());
        services.AddScoped<IProviderAuth>(_ => new StubProviderAuth("test-token"));

        ServiceProvider sp = services.BuildServiceProvider();

        WorkerOptions options = new()
        {
            Image = "test-image:latest",
            MaxConcurrent = 3,
            ConfigPath = "/tmp/config",
            ReportsPath = reportsPath,
            ApiKey = "test-api-key",
            TimeoutMinutes = 99999,
        };

        return new WorkerDispatchService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<WorkerDispatchService>.Instance);
    }

    [Fact]
    public async Task WhenReportFilesExist_IngestionCreatesWorkerReportEntities()
    {
        // Arrange
        ActiveRun activeRun = SeedActiveRun();
        WriteReportFile(activeRun.Id, 1, new
        {
            type = "progress",
            status = "in_progress",
            summary = "Finished step 1",
            error = (string?)null,
            prUrl = (string?)null,
            branchName = "feat/add-login",
            metrics = new { testsRun = 5, testsPassed = 5 },
        });
        WorkerDispatchService sut = BuildService();

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — a WorkerReport was persisted
        await using FoundryDbContext assertDb = CreateDbContext();
        List<WorkerReport> reports = await assertDb.WorkerReports.ToListAsync(TestContext.Current.CancellationToken);
        reports.Count.ShouldBe(1);
        reports[0].ShouldSatisfyAllConditions(
            () => reports[0].WorkerRunId.ShouldBe(activeRun.Id),
            () => reports[0].SequenceNumber.ShouldBe(1),
            () => reports[0].ReportType.ShouldBe("progress"));
    }

    [Fact]
    public async Task WhenReportFilesExist_LatestProgressIsUpdatedFromSummary()
    {
        // Arrange
        ActiveRun activeRun = SeedActiveRun();
        WriteReportFile(activeRun.Id, 1, new
        {
            type = "progress",
            status = "in_progress",
            summary = "Implementing the feature",
            error = (string?)null,
            prUrl = (string?)null,
            branchName = (string?)null,
            metrics = new { testsRun = 0, testsPassed = 0 },
        });
        WorkerDispatchService sut = BuildService();

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — LatestProgress on the ActiveRun is updated
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun updatedRun = run.ShouldBeOfType<ActiveRun>();
        updatedRun.LatestProgress.ShouldBe("Implementing the feature");
    }

    [Fact]
    public async Task WhenMultipleReportFiles_AllAreIngested()
    {
        // Arrange
        ActiveRun activeRun = SeedActiveRun();
        WriteReportFile(activeRun.Id, 1, new
        {
            type = "progress",
            status = "in_progress",
            summary = "Step 1",
            error = (string?)null,
            prUrl = (string?)null,
            branchName = (string?)null,
            metrics = new { testsRun = 0, testsPassed = 0 },
        });
        WriteReportFile(activeRun.Id, 2, new
        {
            type = "progress",
            status = "in_progress",
            summary = "Step 2",
            error = (string?)null,
            prUrl = (string?)null,
            branchName = (string?)null,
            metrics = new { testsRun = 3, testsPassed = 3 },
        });
        WorkerDispatchService sut = BuildService();

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — both reports were persisted
        await using FoundryDbContext assertDb = CreateDbContext();
        List<WorkerReport> reports = await assertDb.WorkerReports
            .OrderBy(r => r.SequenceNumber)
            .ToListAsync(TestContext.Current.CancellationToken);
        reports.Count.ShouldBe(2);
        reports[0].SequenceNumber.ShouldBe(1);
        reports[1].SequenceNumber.ShouldBe(2);
    }

    [Fact]
    public async Task WhenReportAlreadyIngested_IsNotIngestedAgain()
    {
        // Arrange
        ActiveRun activeRun = SeedActiveRun();
        WriteReportFile(activeRun.Id, 1, new
        {
            type = "progress",
            status = "in_progress",
            summary = "Step 1",
            error = (string?)null,
            prUrl = (string?)null,
            branchName = (string?)null,
            metrics = new { testsRun = 0, testsPassed = 0 },
        });
        WorkerDispatchService sut = BuildService();

        // Act — run two ticks
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — only one WorkerReport was created (idempotent ingestion)
        await using FoundryDbContext assertDb = CreateDbContext();
        List<WorkerReport> reports = await assertDb.WorkerReports.ToListAsync(TestContext.Current.CancellationToken);
        reports.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenFinalReportExists_BranchNameAndPrUrlUsedOnCompletion()
    {
        // Arrange — write a final report, then set container to exited
        ActiveRun activeRun = SeedActiveRun();
        WriteReportFile(activeRun.Id, 1, new
        {
            type = "final",
            status = "succeeded",
            summary = "All done",
            error = (string?)null,
            prUrl = "https://github.com/owner/repo/pull/99",
            branchName = "feat/add-login",
            metrics = new { testsRun = 10, testsPassed = 10 },
        });

        SqliteConnection connection = _connection;
        string reportsPath = _reportsBasePath;

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
        services.AddScoped<IWorkerOrchestrator>(_ => new ExitedStubWorkerOrchestrator(exitCode: 0));
        services.AddScoped<IProviderAuth>(_ => new StubProviderAuth("test-token"));

        ServiceProvider sp = services.BuildServiceProvider();

        WorkerOptions options = new()
        {
            Image = "test-image:latest",
            MaxConcurrent = 3,
            ConfigPath = "/tmp/config",
            ReportsPath = reportsPath,
            ApiKey = "test-api-key",
            TimeoutMinutes = 99999,
        };

        WorkerDispatchService sut = new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<WorkerDispatchService>.Instance);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — CompletedRun contains branch and PR info from final report
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        CompletedRun completedRun = run.ShouldBeOfType<CompletedRun>();
        completedRun.ShouldSatisfyAllConditions(
            () => completedRun.BranchName.ShouldBe("feat/add-login"),
            () => completedRun.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/99"));
    }

    [Fact]
    public async Task WhenReportJsonIsInvalid_ReportIsSkippedAndRetried()
    {
        // Arrange — write invalid JSON
        ActiveRun activeRun = SeedActiveRun();
        string runDir = Path.Combine(_reportsBasePath, activeRun.Id.Value.ToString());
        Directory.CreateDirectory(runDir);
        await File.WriteAllTextAsync(
            Path.Combine(runDir, "report-1.json"),
            "{ invalid json }}}",
            TestContext.Current.CancellationToken);
        WorkerDispatchService sut = BuildService();

        // Act — should not throw
        Exception? exception = await Record.ExceptionAsync(
            () => sut.ExecuteTickAsync(TestContext.Current.CancellationToken));

        // Assert — no exception, no report ingested
        exception.ShouldBeNull();
        await using FoundryDbContext assertDb = CreateDbContext();
        List<WorkerReport> reports = await assertDb.WorkerReports.ToListAsync(TestContext.Current.CancellationToken);
        reports.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNoReportsDirectory_IngestionIsSkippedGracefully()
    {
        // Arrange — do NOT create any report directory for this run
        ActiveRun activeRun = SeedActiveRun();
        WorkerDispatchService sut = BuildService();

        // Act
        Exception? exception = await Record.ExceptionAsync(
            () => sut.ExecuteTickAsync(TestContext.Current.CancellationToken));

        // Assert — no exception thrown
        exception.ShouldBeNull();
    }

    [Fact]
    public async Task WhenReportFileHasMalformedName_FileIsSkippedWithoutError()
    {
        // Arrange — write a file that matches the glob but has a malformed sequence number
        ActiveRun activeRun = SeedActiveRun();
        string runDir = Path.Combine(_reportsBasePath, activeRun.Id.Value.ToString());
        Directory.CreateDirectory(runDir);

        // "report-" with no trailing number
        await File.WriteAllTextAsync(
            Path.Combine(runDir, "report-.json"),
            """{"type":"progress","status":"running","summary":"test"}""",
            TestContext.Current.CancellationToken);

        WorkerDispatchService sut = BuildService();

        // Act — should not throw, malformed file skipped
        Exception? exception = await Record.ExceptionAsync(
            () => sut.ExecuteTickAsync(TestContext.Current.CancellationToken));

        // Assert
        exception.ShouldBeNull();
        await using FoundryDbContext assertDb = CreateDbContext();
        List<WorkerReport> reports = await assertDb.WorkerReports.ToListAsync(TestContext.Current.CancellationToken);
        reports.ShouldBeEmpty();
    }

    private sealed class RunningStubWorkerOrchestrator : IWorkerOrchestrator
    {
        public Task<Result<string>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Fail(new Error("Test.NoDispatch", "No dispatch in report tests")));

        public Task StopAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatus?>(new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null));

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class ExitedStubWorkerOrchestrator(int exitCode) : IWorkerOrchestrator
    {
        public Task<Result<string>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Fail(new Error("Test.NoDispatch", "No dispatch in report tests")));

        public Task StopAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatus?>(
                new WorkerStatus(IsRunning: false, ExitCode: exitCode, FinishedAt: DateTimeOffset.UtcNow));

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
