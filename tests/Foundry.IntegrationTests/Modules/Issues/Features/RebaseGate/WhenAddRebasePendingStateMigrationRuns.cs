using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Features.RebaseGate;

/// <summary>
/// Verifies the AddRebasePendingState migration by running the real EF migration: existing monitored
/// repositories default to <c>rebase_after_run = 1</c>, and rolling back hands rebase-pending issues to the
/// checks gate.
/// </summary>
public sealed class WhenAddRebasePendingStateMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261104174326_AddWorkerRunCacheTokenColumns";
    private const string TargetMigrationId = "20261104193750_AddRebasePendingState";

    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenAddRebasePendingStateMigrationRuns()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"rebase-pending-migration-{Guid.NewGuid():N}.db");
    }

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection, o => o.MigrationsAssembly("Foundry.WebApi"))
            .Options;

        _dbContext = new FoundryDbContext(options, DataProtectionProvider.Create("migration-test"));

        // The rebase_after_run column does not exist yet at the previous migration.
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
    public async Task ExistingRepositories_DefaultRebaseAfterRunToTrue()
    {
        // Arrange
        await InsertRepositoryAsync("owner/repo-a", position: 0);
        await InsertRepositoryAsync("owner/repo-b", position: 1);

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        List<long> values = [];
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT rebase_after_run FROM monitored_repositories;";
        using SqliteDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetInt64(0));
        }

        values.Count.ShouldBe(2);
        values.ShouldAllBe(v => v == 1);
    }

    [Fact]
    public async Task Down_HandsRebasePendingRowsToTheChecksGate()
    {
        // Arrange
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);
        Guid issueId = Guid.NewGuid();
        using (SqliteCommand insert = _connection.CreateCommand())
        {
            insert.CommandText =
                "INSERT INTO issues " +
                "(id, monitored_repository_id, issue_number, title, author, url, labels, state, detected_at, " +
                " worker_run_id, branch_name, pull_request_url, feedback_cutoff_at, entered_rebase_pending_at) " +
                "VALUES ($id, $repoId, 3, 'Rebase pending issue', 'octocat', 'https://github.com/o/r/issues/3', " +
                " '[]', 'rebase_pending', datetime('now'), $workerRunId, 'feat/3-test', " +
                " 'https://github.com/o/r/pull/3', datetime('now', '+1 day'), datetime('now'));";
            insert.Parameters.AddWithValue("$id", issueId.ToString());
            insert.Parameters.AddWithValue("$repoId", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("$workerRunId", Guid.NewGuid().ToString());
            await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);

        // Assert
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT state, entered_checks_pending_at FROM issues WHERE id = $id;";
        command.Parameters.AddWithValue("$id", issueId.ToString());
        using SqliteDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
        reader.GetString(0).ShouldBe("checks_pending");
        (await reader.IsDBNullAsync(1, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    private async Task InsertRepositoryAsync(string slug, int position)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO monitored_repositories (id, slug, host, is_active, position, apply_branch_rules, webhook_hook_id) " +
            "VALUES ($id, $slug, 'github.com', 1, $position, 0, $hookId);";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$slug", slug);
        command.Parameters.AddWithValue("$position", position);
        command.Parameters.AddWithValue("$hookId", Guid.NewGuid().ToString("N"));
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
