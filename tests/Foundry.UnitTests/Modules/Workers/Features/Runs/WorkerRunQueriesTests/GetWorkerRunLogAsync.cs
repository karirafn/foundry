using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Runs.WorkerRunQueriesTests;

public sealed class GetWorkerRunLogAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public GetWorkerRunLogAsync()
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

    private async Task<FailedRun> SeedFailedRunAsync(string? containerOutput, string? transcript = null)
    {
        ActiveRun active = StartingRun.Begin(IssueId.New(), WorkerRunId.New()).Activate(
            ContainerId.From("container-log"),
            BranchName.From("feat/3-log"),
            MonitoredRepositoryId.New());

        FailedRun failed = active.Fail(
            new FailureReason.NonZeroExit(1),
            branchNameOrNull: null,
            containerOutput: containerOutput,
            resultSummary: null);

        _dbContext.Set<WorkerRun>().Add(failed);

        if (transcript is not null)
        {
            _dbContext.Set<WorkerRunTranscript>().Add(
                WorkerRunTranscript.Capture(failed.Id, transcript, DateTimeOffset.UtcNow));
        }

        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        return failed;
    }

    [Fact]
    public async Task WhenRunHasATranscript_ReturnsTheTranscriptAsStored()
    {
        // Arrange
        FailedRun run = await SeedFailedRunAsync(containerOutput: "raw tail", transcript: "rendered transcript");
        WorkerRunQueries sut = new(_dbContext);

        // Act
        WorkerRunLogResult result = await sut.GetWorkerRunLogAsync(run.Id.Value, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<WorkerRunLogResult.TranscriptAvailable>().Transcript.ShouldBe("rendered transcript");
    }

    [Fact]
    public async Task WhenRunHasNoTranscript_FallsBackToTheContainerOutput()
    {
        // Arrange
        FailedRun run = await SeedFailedRunAsync(containerOutput: "raw tail");
        WorkerRunQueries sut = new(_dbContext);

        // Act
        WorkerRunLogResult result = await sut.GetWorkerRunLogAsync(run.Id.Value, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<WorkerRunLogResult.LogAvailable>().LogText.ShouldBe("raw tail");
    }

    [Fact]
    public async Task WhenRunHasNeitherTranscriptNorContainerOutput_ReturnsNoLog()
    {
        // Arrange
        FailedRun run = await SeedFailedRunAsync(containerOutput: null);
        WorkerRunQueries sut = new(_dbContext);

        // Act
        WorkerRunLogResult result = await sut.GetWorkerRunLogAsync(run.Id.Value, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<WorkerRunLogResult.NoLog>();
    }
}
