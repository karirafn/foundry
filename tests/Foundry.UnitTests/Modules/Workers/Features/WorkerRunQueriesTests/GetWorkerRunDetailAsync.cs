using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerRunQueriesTests;

public sealed class GetWorkerRunDetailAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public GetWorkerRunDetailAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<ActiveRun> SeedActiveRunAsync(WorkerRunId? runId = null)
    {
        IssueId issueId = IssueId.New();
        WorkerRunId id = runId ?? WorkerRunId.New();
        StartingRun starting = StartingRun.Begin(issueId, id);
        ActiveRun active = starting.Activate(
            ContainerId.From("container-abc123"),
            BranchName.From("feat/1-active-run"),
            MonitoredRepositoryId.New());

        _dbContext.Set<WorkerRun>().Add(active);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        return active;
    }

    private async Task<FailedRun> SeedFailedRunAsync(
        WorkerRunId? runId = null,
        string? containerOutput = null,
        RunResultSummary? resultSummary = null)
    {
        IssueId issueId = IssueId.New();
        WorkerRunId id = runId ?? WorkerRunId.New();
        StartingRun starting = StartingRun.Begin(issueId, id);
        ActiveRun active = starting.Activate(
            ContainerId.From("container-failed"),
            BranchName.From("feat/2-failed-run"),
            MonitoredRepositoryId.New());

        FailedRun failed = active.Fail(
            new FailureReason.NonZeroExit(1),
            branchNameOrNull: null,
            containerOutput: containerOutput,
            resultSummary: resultSummary);

        _dbContext.Set<WorkerRun>().Add(failed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        return failed;
    }

    private async Task<CompletedRun> SeedCompletedRunAsync(
        WorkerRunId? runId = null,
        RunResultSummary? resultSummary = null)
    {
        IssueId issueId = IssueId.New();
        WorkerRunId id = runId ?? WorkerRunId.New();
        StartingRun starting = StartingRun.Begin(issueId, id);
        ActiveRun active = starting.Activate(
            ContainerId.From("container-completed"),
            BranchName.From("feat/3-completed-run"),
            MonitoredRepositoryId.New());

        CompletedRun completed = active.Complete(
            exitCode: 0,
            branchName: BranchName.From("feat/3-completed-run"),
            pullRequestUrl: null,
            resultSummary: resultSummary);

        _dbContext.Set<WorkerRun>().Add(completed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        return completed;
    }

    [Fact]
    public async Task WhenRunDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        WorkerRunQueries sut = new(_dbContext);
        Guid unknownId = Guid.NewGuid();

        // Act
        Result<WorkerRunDetail> result = await sut.GetWorkerRunDetailAsync(
            unknownId,
            TestContext.Current.CancellationToken);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenActiveRunExists_ReturnsRunningState()
    {
        // Arrange
        WorkerRunQueries sut = new(_dbContext);
        ActiveRun run = await SeedActiveRunAsync();

        // Act
        Result<WorkerRunDetail> result = await sut.GetWorkerRunDetailAsync(
            run.Id.Value,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerRunDetail detail = result.ShouldBeOfType<Result<WorkerRunDetail>.Success>().Value;
        detail.State.ShouldBe("running");
    }

    [Fact]
    public async Task WhenActiveRunExists_ReturnsCoreFields()
    {
        // Arrange
        WorkerRunQueries sut = new(_dbContext);
        ActiveRun run = await SeedActiveRunAsync();

        // Act
        Result<WorkerRunDetail> result = await sut.GetWorkerRunDetailAsync(
            run.Id.Value,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerRunDetail detail = result.ShouldBeOfType<Result<WorkerRunDetail>.Success>().Value;
        detail.ShouldSatisfyAllConditions(
            () => detail.WorkerRunId.ShouldBe(run.Id.Value),
            () => detail.IssueId.ShouldBe(run.IssueId.Value),
            () => detail.HasStoredLog.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenFailedRunExists_ReturnsFailedState()
    {
        // Arrange
        WorkerRunQueries sut = new(_dbContext);
        FailedRun run = await SeedFailedRunAsync();

        // Act
        Result<WorkerRunDetail> result = await sut.GetWorkerRunDetailAsync(
            run.Id.Value,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerRunDetail detail = result.ShouldBeOfType<Result<WorkerRunDetail>.Success>().Value;
        detail.State.ShouldBe("failed");
    }

    [Fact]
    public async Task WhenFailedRunHasNoContainerOutput_HasStoredLogIsFalse()
    {
        // Arrange
        WorkerRunQueries sut = new(_dbContext);
        FailedRun run = await SeedFailedRunAsync(containerOutput: null);

        // Act
        Result<WorkerRunDetail> result = await sut.GetWorkerRunDetailAsync(
            run.Id.Value,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerRunDetail detail = result.ShouldBeOfType<Result<WorkerRunDetail>.Success>().Value;
        detail.HasStoredLog.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenFailedRunHasContainerOutput_HasStoredLogIsTrue()
    {
        // Arrange
        WorkerRunQueries sut = new(_dbContext);
        FailedRun run = await SeedFailedRunAsync(containerOutput: "some container output");

        // Act
        Result<WorkerRunDetail> result = await sut.GetWorkerRunDetailAsync(
            run.Id.Value,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerRunDetail detail = result.ShouldBeOfType<Result<WorkerRunDetail>.Success>().Value;
        detail.HasStoredLog.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenFailedRunHasFailureReason_MapsFailureCategoryAndSummary()
    {
        // Arrange
        WorkerRunQueries sut = new(_dbContext);
        FailedRun run = await SeedFailedRunAsync();

        // Act
        Result<WorkerRunDetail> result = await sut.GetWorkerRunDetailAsync(
            run.Id.Value,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerRunDetail detail = result.ShouldBeOfType<Result<WorkerRunDetail>.Success>().Value;
        detail.ShouldSatisfyAllConditions(
            () => detail.FailureCategory.ShouldBe("non_zero_exit"),
            () => detail.FailureSummary.ShouldBe("Non-zero exit code: 1"));
    }

    [Fact]
    public async Task WhenFailedRunHasResultSummary_MapsResultSummaryFields()
    {
        // Arrange
        RunResultSummary summary = RunResultSummary.Create(
            resultText: "success",
            subtype: "result",
            isError: false,
            durationMs: 5000L,
            numTurns: 3,
            totalCostUsd: 0.05m,
            inputTokens: 100,
            outputTokens: 200);

        WorkerRunQueries sut = new(_dbContext);
        FailedRun run = await SeedFailedRunAsync(resultSummary: summary);

        // Act
        Result<WorkerRunDetail> result = await sut.GetWorkerRunDetailAsync(
            run.Id.Value,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerRunDetail detail = result.ShouldBeOfType<Result<WorkerRunDetail>.Success>().Value;
        detail.ShouldSatisfyAllConditions(
            () => detail.ResultText.ShouldBe("success"),
            () => detail.Subtype.ShouldBe("result"),
            () => detail.IsError.ShouldBe(false),
            () => detail.DurationMs.ShouldBe(5000L),
            () => detail.NumTurns.ShouldBe(3),
            () => detail.TotalCostUsd.ShouldBe(0.05m),
            () => detail.InputTokens.ShouldBe(100),
            () => detail.OutputTokens.ShouldBe(200));
    }

    [Fact]
    public async Task WhenCompletedRunExists_ReturnsCompletedState()
    {
        // Arrange
        WorkerRunQueries sut = new(_dbContext);
        CompletedRun run = await SeedCompletedRunAsync();

        // Act
        Result<WorkerRunDetail> result = await sut.GetWorkerRunDetailAsync(
            run.Id.Value,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerRunDetail detail = result.ShouldBeOfType<Result<WorkerRunDetail>.Success>().Value;
        detail.State.ShouldBe("completed");
    }

    [Fact]
    public async Task WhenActiveRunHasCommitMarkers_MapsCommitMarkers()
    {
        // Arrange
        WorkerRunId id = WorkerRunId.New();
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, id);
        ActiveRun active = starting.Activate(
            ContainerId.From("container-with-commits"),
            BranchName.From("feat/4-commits"),
            MonitoredRepositoryId.New());

        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        CommitMarker marker = CommitMarker.Create(observedAt, "abc123", "feat: add feature");
        active.RecordCommit(marker);

        _dbContext.Set<WorkerRun>().Add(active);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        WorkerRunQueries sut = new(_dbContext);

        // Act
        Result<WorkerRunDetail> result = await sut.GetWorkerRunDetailAsync(
            active.Id.Value,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerRunDetail detail = result.ShouldBeOfType<Result<WorkerRunDetail>.Success>().Value;
        WorkerRunCommitMarker mappedMarker = detail.CommitMarkers.ShouldHaveSingleItem();
        mappedMarker.ShouldSatisfyAllConditions(
            () => mappedMarker.Sha.ShouldBe("abc123"),
            () => mappedMarker.Message.ShouldBe("feat: add feature"));
    }
}
