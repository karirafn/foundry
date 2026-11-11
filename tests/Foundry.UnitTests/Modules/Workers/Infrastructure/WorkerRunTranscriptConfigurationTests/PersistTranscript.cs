using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.WorkerRunTranscriptConfigurationTests;

public sealed class PersistTranscript : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistTranscript()
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

    [Fact]
    public async Task WhenTranscriptPersisted_CanBeReloadedWithAllProperties()
    {
        // Arrange
        CompletedRun run = StartingRun.Begin(IssueId.New(), WorkerRunId.New())
            .Activate(ContainerId.From("container-persist"), BranchName.From("feat/5-persist"), MonitoredRepositoryId.New())
            .Complete(exitCode: 0, branchName: BranchName.From("feat/5-persist"), pullRequestUrl: null);
        DateTimeOffset capturedAt = new(2026, 11, 10, 12, 30, 15, TimeSpan.Zero);
        _dbContext.Set<WorkerRun>().Add(run);
        _dbContext.Set<WorkerRunTranscript>().Add(WorkerRunTranscript.Capture(run.Id, "rendered transcript", capturedAt));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRunTranscript reloaded = await _dbContext.Set<WorkerRunTranscript>()
            .SingleAsync(TestContext.Current.CancellationToken);

        // Assert
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.Id.ShouldBe(run.Id),
            () => reloaded.Transcript.ShouldBe("rendered transcript"),
            () => reloaded.CapturedAt.ShouldBe(capturedAt));
    }

    [Fact]
    public async Task WhenTranscriptHasNoRun_IsRejectedByTheForeignKey()
    {
        // Arrange
        _dbContext.Set<WorkerRunTranscript>().Add(
            WorkerRunTranscript.Capture(WorkerRunId.New(), "orphan", DateTimeOffset.UtcNow));

        // Act + Assert
        await Should.ThrowAsync<DbUpdateException>(
            () => _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }
}
