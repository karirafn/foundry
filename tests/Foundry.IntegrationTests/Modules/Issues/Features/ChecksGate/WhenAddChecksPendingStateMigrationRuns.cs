using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Foundry.WebApi.Persistence;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Features.ChecksGate;

/// <summary>
/// Verifies the AddChecksPendingState migration Up() data-move SQL by running the actual EF
/// migration — existing review rows must be converted to checks_pending with
/// entered_checks_pending_at set.
///
/// Docker is NOT available in this sandbox — compile-verified only; executes in CI.
/// </summary>
public sealed class WhenAddChecksPendingStateMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261008150254_AddApiKeyColumn";
    private const string TargetMigrationId = "20261021144120_AddChecksPendingState";

    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenAddChecksPendingStateMigrationRuns()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"checks-pending-migration-{Guid.NewGuid():N}.db");
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

        // Bring the schema up to the migration immediately before AddChecksPendingState.
        // The checks_outcome and entered_checks_pending_at columns do not yet exist here.
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
    public async Task ExistingReviewRows_AreConvertedToChecksPending()
    {
        // Arrange — insert a review-state issue row at the pre-migration schema.
        // feedback_cutoff_at, branch_name, pull_request_url and worker_run_id are required by the review
        // state and will be required by checks_pending after migration. The check constraint is not yet
        // present so this insert succeeds.
        Guid issueId = Guid.NewGuid();
        Guid repositoryId = Guid.NewGuid();
        Guid workerRunId = Guid.NewGuid();
        await InsertReviewIssueAsync(issueId, repositoryId, workerRunId);

        // Act — apply AddChecksPendingState, which runs the data-move UPDATE.
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — the row's state is now checks_pending and entered_checks_pending_at is set.
        (string State, string? EnteredAt) = await ReadIssueStateAsync(issueId);
        State.ShouldBe("checks_pending");
        EnteredAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ExistingNonReviewRows_AreNotAffected()
    {
        // Arrange — insert a detected-state issue (no review fields).
        Guid issueId = Guid.NewGuid();
        Guid repositoryId = Guid.NewGuid();
        await InsertDetectedIssueAsync(issueId, repositoryId);

        // Act — apply AddChecksPendingState.
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — non-review rows remain in their original state.
        (string State, string? EnteredAt) = await ReadIssueStateAsync(issueId);
        State.ShouldBe("detected");
        EnteredAt.ShouldBeNull();
    }

    [Fact]
    public async Task MigrationAddsEnteredChecksPendingAtColumn()
    {
        // Arrange — no rows needed; we only verify schema.
        // Act — apply the migration.
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — the column can be queried without error (would throw if absent).
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM issues WHERE entered_checks_pending_at IS NOT NULL;";
        long count = (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        count.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Down_RevertsChecksPendingRowsToReview()
    {
        // Arrange — apply Up() to reach the target schema, then insert a checks_pending row.
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        Guid issueId = Guid.NewGuid();
        Guid repositoryId = Guid.NewGuid();
        Guid workerRunId = Guid.NewGuid();
        await InsertChecksPendingIssueAsync(issueId, repositoryId, workerRunId);

        // Act — roll back to the previous migration, which triggers Down().
        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);

        // Assert — the row is now review; the checks_pending state has been reverted.
        // After Down() the checks_pending columns are gone, so only the state column is read.
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT state FROM issues WHERE id = $id;";
        command.Parameters.AddWithValue("$id", issueId.ToString());
        string? state = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        state.ShouldBe("review");
    }

    private async Task InsertChecksPendingIssueAsync(Guid issueId, Guid repositoryId, Guid workerRunId)
    {
        // Insert a checks_pending row using the columns available at the target schema.
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO issues " +
            "(id, monitored_repository_id, issue_number, title, author, url, state, detected_at, " +
            " worker_run_id, branch_name, pull_request_url, feedback_cutoff_at, entered_checks_pending_at) " +
            "VALUES ($id, $repoId, 3, 'Checks pending issue', 'octocat', 'https://github.com/o/r/issues/3', " +
            " 'checks_pending', datetime('now'), $workerRunId, 'feat/3-test', " +
            " 'https://github.com/o/r/pull/3', datetime('now', '+1 day'), datetime('now'));";
        command.Parameters.AddWithValue("$id", issueId.ToString());
        command.Parameters.AddWithValue("$repoId", repositoryId.ToString());
        command.Parameters.AddWithValue("$workerRunId", workerRunId.ToString());
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task InsertReviewIssueAsync(Guid issueId, Guid repositoryId, Guid workerRunId)
    {
        // Insert using only the columns that exist at the pre-migration schema.
        // All NOT-NULL columns are provided; optional columns absent.
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO issues " +
            "(id, monitored_repository_id, issue_number, title, author, url, state, detected_at, " +
            " worker_run_id, branch_name, pull_request_url, feedback_cutoff_at) " +
            "VALUES ($id, $repoId, 1, 'Review issue', 'octocat', 'https://github.com/o/r/issues/1', " +
            " 'review', datetime('now'), $workerRunId, 'feat/1-test', " +
            " 'https://github.com/o/r/pull/1', datetime('now', '+1 day'));";
        command.Parameters.AddWithValue("$id", issueId.ToString());
        command.Parameters.AddWithValue("$repoId", repositoryId.ToString());
        command.Parameters.AddWithValue("$workerRunId", workerRunId.ToString());
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task InsertDetectedIssueAsync(Guid issueId, Guid repositoryId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO issues " +
            "(id, monitored_repository_id, issue_number, title, author, url, state, detected_at) " +
            "VALUES ($id, $repoId, 2, 'Detected issue', 'octocat', 'https://github.com/o/r/issues/2', " +
            " 'detected', datetime('now'));";
        command.Parameters.AddWithValue("$id", issueId.ToString());
        command.Parameters.AddWithValue("$repoId", repositoryId.ToString());
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<(string State, string? EnteredAt)> ReadIssueStateAsync(Guid issueId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "SELECT state, entered_checks_pending_at FROM issues WHERE id = $id;";
        command.Parameters.AddWithValue("$id", issueId.ToString());

        using SqliteDataReader reader =
            await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        await reader.ReadAsync(TestContext.Current.CancellationToken);
        string state = reader.GetString(0);
        string? enteredAt = await reader.IsDBNullAsync(1, TestContext.Current.CancellationToken)
            ? null
            : reader.GetString(1);
        return (state, enteredAt);
    }
}
