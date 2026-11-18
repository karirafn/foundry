using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Persistence;

/// <summary>
/// The AddImplementRunOrigin migration (#1060) backfills existing implement runs as fresh: every in_progress row, and
/// publish_blocked / publish_failed rows that hold an implement run (no revision trigger). Revision and other rows stay
/// without an origin. Runs locally against SQLite — no Docker required.
/// </summary>
public sealed class WhenAddImplementRunOriginMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261117172548_AddIssueWatchBranchHead";
    private const string TargetMigrationId = "20261117184054_AddImplementRunOrigin";
    private const string RevisionTriggerJson = """{"discriminator":"rebase_conflict"}""";

    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenAddImplementRunOriginMigrationRuns()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"implement-run-origin-migration-{Guid.NewGuid():N}.db");
    }

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

    [Theory]
    [InlineData("in_progress", false, "fresh")]
    [InlineData("publish_blocked", false, "fresh")]
    [InlineData("publish_failed", false, "fresh")]
    [InlineData("publish_blocked", true, null)]
    [InlineData("publish_failed", true, null)]
    [InlineData("revision_in_progress", true, null)]
    [InlineData("detected", false, null)]
    public async Task ExistingRow_IsBackfilledOnlyWhenItHoldsAnImplementRun(
        string state,
        bool isRevision,
        string? expectedOrigin)
    {
        // Arrange
        Guid issueId = Guid.NewGuid();
        await InsertRowAsync(issueId, state, isRevision);

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await ReadOriginAsync(issueId)).ShouldBe(expectedOrigin);
    }

    private async Task InsertRowAsync(Guid issueId, string state, bool isRevision)
    {
        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO issues " +
            "(id, monitored_repository_id, issue_number, title, author, url, labels, state, detected_at, entered_state_at, " +
            " worker_run_id, branch_name, pull_request_url, failure_reason, failed_at, publish_blocked_at, " +
            " revision_trigger) " +
            "VALUES ($id, $repoId, 7, 'Issue', 'octocat', 'https://github.com/o/r/issues/7', '[]', $state, datetime('now'), " +
            " datetime('now'), $runId, 'feat/7-test', 'https://github.com/o/r/pull/8', 'push refused', " +
            " datetime('now'), datetime('now'), $trigger);";
        command.Parameters.AddWithValue("$id", issueId.ToString());
        command.Parameters.AddWithValue("$repoId", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$runId", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$trigger", isRevision ? RevisionTriggerJson : DBNull.Value);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string?> ReadOriginAsync(Guid issueId)
    {
        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT implement_run_origin FROM issues WHERE id = $id;";
        command.Parameters.AddWithValue("$id", issueId.ToString());
        object? value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is DBNull ? null : (string?)value;
    }
}
