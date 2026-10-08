using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Persistence.RepositoryMaxConcurrentWorkersBackfillTests;

/// <summary>
/// Verifies the real AddRepositoryMaxConcurrentWorkers migration Up() backfill SQL by running
/// the actual EF migration rather than copy-pasting the SQL inline.
/// </summary>
public sealed class WhenMigrationRunsOnExistingRepositories : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20260917202507_RewriteLegacyProcessedEventTimestamps";
    private const string TargetMigrationId = "20261001182529_AddRepositoryMaxConcurrentWorkers";

    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenMigrationRunsOnExistingRepositories()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"migration-test-{Guid.NewGuid():N}.db");
    }

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection, o => o.MigrationsAssembly("Foundry.WebApi"))
            .Options;

        _dbContext = new FoundryDbContext(
            options,
            DataProtectionProvider.Create("migration-test"));

        // Bring the schema up to the migration immediately before AddRepositoryMaxConcurrentWorkers.
        // This ensures the max_concurrent_workers column does not yet exist when we seed test rows.
        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task BackfilledMaxConcurrentWorkers_AreAllOne()
    {
        // Arrange — insert three repositories without the max_concurrent_workers column (it does not exist yet).
        // Assign unique positions to satisfy the ix_monitored_repositories_position unique index (added by
        // the AddRepositoryPosition migration, which runs before this one).
        // account_id was dropped from monitored_repositories by 20260718213128_DropMonitoredRepositoryAccountIdFk,
        // which runs before this migration — no FK column is present in the pre-migration schema.
        await InsertRepositoryAsync("owner/repo-a", position: 0);
        await InsertRepositoryAsync("owner/repo-b", position: 1);
        await InsertRepositoryAsync("owner/repo-c", position: 2);

        // Act — apply AddRepositoryMaxConcurrentWorkers, which runs the real Up() backfill SQL.
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — every existing row has max_concurrent_workers = 1 after the backfill.
        List<int> maxConcurrentWorkerValues = await LoadMaxConcurrentWorkersAsync();

        maxConcurrentWorkerValues.ShouldNotBeEmpty();
        maxConcurrentWorkerValues.ShouldAllBe(v => v == 1);
    }

    [Fact]
    public async Task BackfilledMaxConcurrentWorkers_AreOneForEveryRow()
    {
        // Arrange — insert two repositories without the new column, with unique positions.
        await InsertRepositoryAsync("org/service-one", position: 0);
        await InsertRepositoryAsync("org/service-two", position: 1);

        // Act — apply the real migration.
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — row count is preserved and each row has the backfilled value of 1.
        List<int> maxConcurrentWorkerValues = await LoadMaxConcurrentWorkersAsync();

        maxConcurrentWorkerValues.Count.ShouldBe(2);
        maxConcurrentWorkerValues.ShouldAllBe(v => v == 1);
    }

    /// <summary>
    /// Inserts a monitored_repository row via raw ADO.NET using only the pre-migration columns
    /// (no max_concurrent_workers — the column does not exist yet at the point of insertion).
    /// The account_id column was dropped by 20260718213128_DropMonitoredRepositoryAccountIdFk, which
    /// runs before this migration, so it must not appear in this INSERT.
    /// A unique <paramref name="position"/> is required because the AddRepositoryPosition migration,
    /// which runs before this one, adds a unique index on the position column.
    /// </summary>
    private async Task<Guid> InsertRepositoryAsync(string slug, int position)
    {
        Guid id = Guid.NewGuid();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO monitored_repositories (id, slug, host, is_active, position) " +
            "VALUES ($id, $slug, 'github.com', 1, $position);";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$slug", slug);
        command.Parameters.AddWithValue("$position", position);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private async Task<List<int>> LoadMaxConcurrentWorkersAsync()
    {
        List<int> results = [];
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT max_concurrent_workers FROM monitored_repositories ORDER BY id;";
        using SqliteDataReader reader =
            await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            results.Add(reader.GetInt32(0));
        }

        return results;
    }
}
