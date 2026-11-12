using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.WorkerRunConfigurationTests;

public sealed class PersistSuppliedOpenIssues : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistSuppliedOpenIssues()
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
    public async Task WhenRunHasSuppliedOpenIssues_ReloadsTheSameNumbers()
    {
        // Arrange
        StartingRun run = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        TrackerContext trackerContext = new TrackerContextBuilder()
            .WithOpenIssueNumbers(7, 12, 31)
            .Build();
        run.RecordSuppliedOpenIssues(trackerContext);

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        WorkerRun reloaded = result.ShouldNotBeNull();
        reloaded.SuppliedOpenIssueNumbers.ShouldBe(run.SuppliedOpenIssueNumbers.ShouldNotBeNull());
    }

    [Fact]
    public async Task WhenRunHasSuppliedOpenIssues_StoresThemAsJsonArrayInSuppliedOpenIssuesColumn()
    {
        // Arrange
        StartingRun run = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        TrackerContext trackerContext = new TrackerContextBuilder()
            .WithOpenIssueNumbers(7, 12, 31)
            .Build();
        run.RecordSuppliedOpenIssues(trackerContext);

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        string? stored = await _dbContext.Database
            .SqlQueryRaw<string>("SELECT supplied_open_issues AS Value FROM worker_runs")
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);

        // Assert
        stored.ShouldBe("[7,12,31]");
    }

    [Fact]
    public async Task WhenRunHasNoSuppliedOpenIssues_ReloadsNull()
    {
        // Arrange
        StartingRun run = StartingRun.Begin(IssueId.New(), WorkerRunId.New());

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        WorkerRun reloaded = result.ShouldNotBeNull();
        reloaded.SuppliedOpenIssueNumbers.ShouldBeNull();
    }
}
