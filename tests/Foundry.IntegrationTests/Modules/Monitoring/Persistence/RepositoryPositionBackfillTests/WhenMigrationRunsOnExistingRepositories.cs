using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Persistence.RepositoryPositionBackfillTests;

/// <summary>
/// Verifies the real AddRepositoryPosition migration Up() backfill SQL by running
/// the actual EF migration rather than copy-pasting the SQL inline.
/// </summary>
public sealed class WhenMigrationRunsOnExistingRepositories : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20260625210117_RemoveDismissedIssueState";
    private const string TargetMigrationId = "20260626221728_AddRepositoryPosition";

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

        // Bring the schema up to the migration immediately before AddRepositoryPosition.
        // This ensures the position column does not yet exist when we seed test rows.
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
    public async Task BackfilledPositions_AreContiguousAndUnique()
    {
        // Arrange — insert three repositories without a position column (it does not exist yet).
        Guid accountId = await InsertAccountAsync();
        await InsertRepositoryAsync(accountId, "owner/repo-a");
        await InsertRepositoryAsync(accountId, "owner/repo-b");
        await InsertRepositoryAsync(accountId, "owner/repo-c");

        // Act — apply AddRepositoryPosition, which runs the real Up() backfill SQL.
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — every row has a unique position; together they form 0..n-1.
        List<(Guid Id, int Position)> rows = await LoadRepositoryPositionsAsync();

        List<int> positions = rows
            .Select(r => r.Position)
            .OrderBy(p => p)
            .ToList();

        positions.ShouldBe([0, 1, 2]);
        positions.Distinct()
            .Count()
            .ShouldBe(3);
    }

    [Fact]
    public async Task BackfilledPositions_AreOrderedByIdAscending()
    {
        // Arrange — insert two repositories without position.
        Guid accountId = await InsertAccountAsync();
        await InsertRepositoryAsync(accountId, "org/alpha");
        await InsertRepositoryAsync(accountId, "org/beta");

        // Act — apply the real migration.
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — the row with the lexicographically smaller id gets position 0.
        List<(Guid Id, int Position)> rows = await LoadRepositoryPositionsAsync();

        List<(Guid Id, int Position)> orderedById = rows
            .OrderBy(r => r.Id.ToString())
            .ToList();

        for (int i = 0; i < orderedById.Count; i++)
        {
            (Guid id, int position) = orderedById[i];
            position.ShouldBe(i, $"repository with id {id} (id-order rank {i}) should have position {i}");
        }
    }

    [Fact]
    public async Task UniqueIndexOnPosition_ExistsAfterMigration()
    {
        // Arrange
        Guid accountId = await InsertAccountAsync();
        await InsertRepositoryAsync(accountId, "org/repo-x");
        await InsertRepositoryAsync(accountId, "org/repo-y");

        // Act — apply the real migration.
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — setting all rows to the same position violates the unique index.
        await Should.ThrowAsync<SqliteException>(async () =>
            await _dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE monitored_repositories SET position = 0;",
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Inserts an account row via a raw ADO.NET command, bypassing EF's model shape
    /// and EF1002 SQL injection analysis (test data only — all values are constants).
    /// </summary>
    private async Task<Guid> InsertAccountAsync()
    {
        Guid id = Guid.NewGuid();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO accounts (id, name, token, base_url, type) " +
            "VALUES ($id, 'Test Account', NULL, 'https://github.com', 'github');";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    /// <summary>
    /// Inserts a monitored_repository row via raw ADO.NET using only the pre-migration columns
    /// (no position — the column does not exist yet at the point of insertion).
    /// </summary>
    private async Task<Guid> InsertRepositoryAsync(Guid accountId, string slug)
    {
        Guid id = Guid.NewGuid();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO monitored_repositories (id, account_id, slug, host, is_active, eligibility_status) " +
            "VALUES ($id, $accountId, $slug, 'github.com', 1, 'unreachable');";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$accountId", accountId.ToString());
        command.Parameters.AddWithValue("$slug", slug);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private async Task<List<(Guid Id, int Position)>> LoadRepositoryPositionsAsync()
    {
        List<(Guid, int)> results = [];
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT id, position FROM monitored_repositories ORDER BY id;";
        using SqliteDataReader reader =
            await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            Guid id = Guid.Parse(reader.GetString(0));
            int position = reader.GetInt32(1);
            results.Add((id, position));
        }

        return results;
    }
}
