using Foundry.Modules.Workers.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Persistence;

/// <summary>
/// The AddWorkerRunModelAndPlanUsage migration adds the dispatch model and plan usage columns to <c>worker_runs</c>
/// without changing a stored run. Runs locally against SQLite.
/// </summary>
public sealed class WhenAddWorkerRunModelAndPlanUsageMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261126143612_AddImplementTierSettings";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"run-model-migration-{Guid.NewGuid():N}.db");
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection, o => o.MigrationsAssembly("Foundry.WebApi"))
            .Options;

        _dbContext = new FoundryDbContext(options, DataProtectionProvider.Create("migration-test"));

        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task AStoredRunReadsAsHavingNoDemandModelEffortOrPlanUsage()
    {
        // Arrange
        Guid runId = Guid.NewGuid();
        await _dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO worker_runs (id, issue_id, created_at, state, role)
            VALUES ({runId}, {Guid.NewGuid()}, '2026-11-20T10:00:00.0000000+00:00', 'starting', 'implement')
            """,
            TestContext.Current.CancellationToken);

        // Act
        await _dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRun run = await _dbContext.Set<WorkerRun>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        run.ShouldSatisfyAllConditions(
            () => run.Demand.ShouldBeNull(),
            () => run.Model.ShouldBeNull(),
            () => run.Effort.ShouldBeNull(),
            () => run.PlanUsageAtStart.ShouldBeNull(),
            () => run.PlanUsageAtEnd.ShouldBeNull());
    }
}
