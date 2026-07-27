using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.WorkerRunConfigurationTests;

public sealed class PersistActiveRunActivity : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistActiveRunActivity()
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
    public async Task WhenActiveRunHasActivity_LastActivityAtRoundTrips()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun run = starting.Activate(
            ContainerId.From("container-activity"),
            BranchName.From("feat/1-activity"),
            MonitoredRepositoryId.New());

        DateTimeOffset activityAt = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        run.RecordActivity(activityAt);

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        ActiveRun reloaded = result.ShouldBeOfType<ActiveRun>();
        reloaded.LastActivityAt.ShouldBe(activityAt);
    }

    [Fact]
    public async Task WhenActiveRunHasCommitMarkers_CommitMarkersRoundTrip()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun run = starting.Activate(
            ContainerId.From("container-commits"),
            BranchName.From("feat/2-commits"),
            MonitoredRepositoryId.New());

        CommitMarker marker = CommitMarker.Create(
            observedAt: new DateTimeOffset(2026, 6, 29, 13, 0, 0, TimeSpan.Zero),
            sha: "abc123def456",
            message: "feat: add something");

        run.RecordCommit(marker);

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        ActiveRun reloaded = result.ShouldBeOfType<ActiveRun>();
        reloaded.CommitMarkers.Count.ShouldBe(1);
        CommitMarker reloadedMarker = reloaded.CommitMarkers[0];
        reloadedMarker.ShouldSatisfyAllConditions(
            () => reloadedMarker.Sha.ShouldBe("abc123def456"),
            () => reloadedMarker.Message.ShouldBe("feat: add something"),
            () => reloadedMarker.ObservedAt.ShouldBe(new DateTimeOffset(2026, 6, 29, 13, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task WhenActiveRunHasNoActivity_LastActivityAtIsNull()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun run = starting.Activate(
            ContainerId.From("container-no-activity"),
            BranchName.From("feat/3-no-activity"),
            MonitoredRepositoryId.New());

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        ActiveRun reloaded = result.ShouldBeOfType<ActiveRun>();
        reloaded.LastActivityAt.ShouldBeNull();
    }
}
