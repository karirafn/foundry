using System.Text.Json;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class ReportIngestion : WorkerDispatchServiceTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _reportsBasePath;

    public ReportIngestion()
    {
        _reportsBasePath = Path.Combine(Path.GetTempPath(), $"foundry-reports-{Guid.NewGuid()}");
        Directory.CreateDirectory(_reportsBasePath);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (Directory.Exists(_reportsBasePath))
        {
            Directory.Delete(_reportsBasePath, recursive: true);
        }

        await base.DisposeAsyncCore();
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

    private WorkerDispatchService BuildReportService(
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher? integrationEventDispatcher = null)
    {
        WorkerOptions options = new()
        {
            Image = "test-image:latest",
            MaxConcurrent = 3,
            ReportsPath = _reportsBasePath,
            ApiKey = "test-api-key",
            TimeoutMinutes = 99999,
        };

        // Delegates to base.BuildService — accesses inherited instance state.
        return base.BuildService(orchestrator, options, integrationEventDispatcher);
    }

    [Fact]
    public async Task WhenReportFilesExist_IngestionCreatesWorkerReportEntities()
    {
        // Arrange
        ActiveRun activeRun = SeedActiveRun("container-report-test");
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
        WorkerDispatchService sut = BuildReportService(new RunningStubWorkerOrchestrator());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — a WorkerReport was persisted
        await using FoundryDbContext assertDb = CreateDbContext();
        List<WorkerReport> reports = await assertDb.Set<WorkerReport>().ToListAsync(TestContext.Current.CancellationToken);
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
        ActiveRun activeRun = SeedActiveRun("container-report-test");
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
        WorkerDispatchService sut = BuildReportService(new RunningStubWorkerOrchestrator());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — LatestProgress on the ActiveRun is updated
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun updatedRun = run.ShouldBeOfType<ActiveRun>();
        updatedRun.LatestProgress.ShouldBe("Implementing the feature");
    }

    [Fact]
    public async Task WhenMultipleReportFiles_AllAreIngested()
    {
        // Arrange
        ActiveRun activeRun = SeedActiveRun("container-report-test");
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
        WorkerDispatchService sut = BuildReportService(new RunningStubWorkerOrchestrator());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — both reports were persisted
        await using FoundryDbContext assertDb = CreateDbContext();
        List<WorkerReport> reports = await assertDb.Set<WorkerReport>()
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
        ActiveRun activeRun = SeedActiveRun("container-report-test");
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
        WorkerDispatchService sut = BuildReportService(new RunningStubWorkerOrchestrator());

        // Act — run two ticks
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — only one WorkerReport was created (idempotent ingestion)
        await using FoundryDbContext assertDb = CreateDbContext();
        List<WorkerReport> reports = await assertDb.Set<WorkerReport>().ToListAsync(TestContext.Current.CancellationToken);
        reports.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenFinalReportExists_BranchNameAndPrUrlUsedOnCompletion()
    {
        // Arrange — write a final report, then set container to exited
        ActiveRun activeRun = SeedActiveRun("container-report-test");
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
        WorkerDispatchService sut = BuildReportService(new ExitedStubWorkerOrchestrator(exitCode: 0));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — CompletedRun contains branch and PR info from final report
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        CompletedRun completedRun = run.ShouldBeOfType<CompletedRun>();
        completedRun.ShouldSatisfyAllConditions(
            () => completedRun.BranchName.ShouldBe(BranchName.From("feat/add-login")),
            () => completedRun.PullRequestUrl.ShouldBe(PullRequestUrl.From("https://github.com/owner/repo/pull/99")));
    }

    [Fact]
    public async Task WhenOnlyBranchCreatedReportIngested_CompletedRunHasBranchName()
    {
        // Arrange — write only a branch-created report (no final report), then container exits with code 0
        ActiveRun activeRun = SeedActiveRun("container-branch-only-complete");
        WriteReportFile(activeRun.Id, 1, new
        {
            type = "branch-created",
            status = "in_progress",
            summary = "Branch created",
            error = (string?)null,
            prUrl = (string?)null,
            branchName = "feat/102-feature",
            metrics = (object?)null,
        });
        WorkerDispatchService sut = BuildReportService(new ExitedStubWorkerOrchestrator(exitCode: 0));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — CompletedRun falls back to branch name from the branch-created report
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        CompletedRun completedRun = run.ShouldBeOfType<CompletedRun>();
        completedRun.BranchName.ShouldBe(BranchName.From("feat/102-feature"));
    }

    [Fact]
    public async Task WhenReportJsonIsInvalid_ReportIsSkippedAndRetried()
    {
        // Arrange — write invalid JSON
        ActiveRun activeRun = SeedActiveRun("container-report-test");
        string runDir = Path.Combine(_reportsBasePath, activeRun.Id.Value.ToString());
        Directory.CreateDirectory(runDir);
        await File.WriteAllTextAsync(
            Path.Combine(runDir, "report-1.json"),
            "{ invalid json }}}",
            TestContext.Current.CancellationToken);
        WorkerDispatchService sut = BuildReportService(new RunningStubWorkerOrchestrator());

        // Act — should not throw
        Exception? exception = await Record.ExceptionAsync(
            () => sut.ExecuteTickAsync(TestContext.Current.CancellationToken));

        // Assert — no exception, no report ingested
        exception.ShouldBeNull();
        await using FoundryDbContext assertDb = CreateDbContext();
        List<WorkerReport> reports = await assertDb.Set<WorkerReport>().ToListAsync(TestContext.Current.CancellationToken);
        reports.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNoReportsDirectory_IngestionIsSkippedGracefully()
    {
        // Arrange — do NOT create any report directory for this run
        SeedActiveRun("container-report-test");
        WorkerDispatchService sut = BuildReportService(new RunningStubWorkerOrchestrator());

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
        ActiveRun activeRun = SeedActiveRun("container-report-test");
        string runDir = Path.Combine(_reportsBasePath, activeRun.Id.Value.ToString());
        Directory.CreateDirectory(runDir);

        // "report-" with no trailing number
        await File.WriteAllTextAsync(
            Path.Combine(runDir, "report-.json"),
            """{"type":"progress","status":"running","summary":"test"}""",
            TestContext.Current.CancellationToken);

        WorkerDispatchService sut = BuildReportService(new RunningStubWorkerOrchestrator());

        // Act — should not throw, malformed file skipped
        Exception? exception = await Record.ExceptionAsync(
            () => sut.ExecuteTickAsync(TestContext.Current.CancellationToken));

        // Assert
        exception.ShouldBeNull();
        await using FoundryDbContext assertDb = CreateDbContext();
        List<WorkerReport> reports = await assertDb.Set<WorkerReport>().ToListAsync(TestContext.Current.CancellationToken);
        reports.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenBranchCreatedReportIngested_BranchNameIsSetOnActiveRun()
    {
        // Arrange
        ActiveRun activeRun = SeedActiveRun("container-branch-test");
        WriteReportFile(activeRun.Id, 1, new
        {
            type = "branch-created",
            status = "in_progress",
            summary = "Branch created",
            error = (string?)null,
            prUrl = (string?)null,
            branchName = "feat/102-my-feature",
            metrics = (object?)null,
        });
        WorkerDispatchService sut = BuildReportService(new RunningStubWorkerOrchestrator());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun updatedRun = run.ShouldBeOfType<ActiveRun>();
        updatedRun.BranchName.ShouldBe(BranchName.From("feat/102-my-feature"));
    }

    [Fact]
    public async Task WhenMultipleReportsWithBranchName_FirstWriteWins()
    {
        // Arrange
        ActiveRun activeRun = SeedActiveRun("container-first-write-wins");
        WriteReportFile(activeRun.Id, 1, new
        {
            type = "branch-created",
            status = "in_progress",
            summary = "Branch created",
            error = (string?)null,
            prUrl = (string?)null,
            branchName = "feat/102-first-branch",
            metrics = (object?)null,
        });
        WriteReportFile(activeRun.Id, 2, new
        {
            type = "progress",
            status = "in_progress",
            summary = "Still working",
            error = (string?)null,
            prUrl = (string?)null,
            branchName = "feat/102-second-branch",
            metrics = (object?)null,
        });
        WorkerDispatchService sut = BuildReportService(new RunningStubWorkerOrchestrator());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — first branch name is retained
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun updatedRun = run.ShouldBeOfType<ActiveRun>();
        updatedRun.BranchName.ShouldBe(BranchName.From("feat/102-first-branch"));
    }

    [Fact]
    public async Task WhenRunFailsAfterBranchReport_FailedRunHasBranchName()
    {
        // Arrange — write a branch-created report, then set container to exited with non-zero exit code
        ActiveRun activeRun = SeedActiveRun("container-fail-with-branch");
        WriteReportFile(activeRun.Id, 1, new
        {
            type = "branch-created",
            status = "in_progress",
            summary = "Branch created",
            error = (string?)null,
            prUrl = (string?)null,
            branchName = "feat/102-work-in-progress",
            metrics = (object?)null,
        });
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildReportService(new ExitedStubWorkerOrchestrator(exitCode: 1), dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — FailedRun carries the branch name from the ingested report
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.BranchName.ShouldBe(BranchName.From("feat/102-work-in-progress"));

        // Assert — WorkerRunFailed integration event carries branch name
        WorkerRunFailed failedEvent = dispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.BranchName.ShouldBe("feat/102-work-in-progress");
    }

    private sealed class RunningStubWorkerOrchestrator : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in report tests")));

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

    private sealed class ExitedStubWorkerOrchestrator(int exitCode) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in report tests")));

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
