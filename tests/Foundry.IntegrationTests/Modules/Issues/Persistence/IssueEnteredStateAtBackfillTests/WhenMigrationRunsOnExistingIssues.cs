using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Persistence.IssueEnteredStateAtBackfillTests;

/// <summary>
/// Verifies the real AddIssueEnteredStateAt migration Up() SQL against rows seeded through raw ADO.NET before it runs:
/// every existing issue enters its current state at its detection time (#892).
/// </summary>
public sealed class WhenMigrationRunsOnExistingIssues : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261113124524_AddHostPausesAndPreparedRepository";
    private const string TargetMigrationId = "20261113142738_AddIssueEnteredStateAt";

    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenMigrationRunsOnExistingIssues()
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

    [Theory]
    [InlineData("triage_queued", "2026-11-01T08:15:00.0000000Z")]
    [InlineData("queued", "2026-10-20T17:42:31.1234567Z")]
    public async Task ExistingIssue_EntersItsStateAtItsDetectionTime(string state, string detectedAt)
    {
        // Arrange
        Guid id = await InsertIssueAsync(state, detectedAt);

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await LoadEnteredStateAtAsync(id)).ShouldBe(detectedAt);
    }

    private async Task<Guid> InsertIssueAsync(string state, string detectedAt)
    {
        Guid id = Guid.NewGuid();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO issues (id, monitored_repository_id, issue_number, title, author, url, labels, detected_at, " +
            "state, blocked_by, clarifications) " +
            "VALUES ($id, $repositoryId, 7, 'An issue', 'octocat', 'https://github.com/owner/repo/issues/7', " +
            "'[\"foundry\"]', $detectedAt, $state, '[]', '[]');";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$repositoryId", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$detectedAt", detectedAt);
        command.Parameters.AddWithValue("$state", state);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private async Task<string?> LoadEnteredStateAtAsync(Guid id)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT entered_state_at FROM issues WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        return (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}
