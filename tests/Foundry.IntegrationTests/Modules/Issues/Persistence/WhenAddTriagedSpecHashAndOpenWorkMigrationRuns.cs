using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Persistence;

/// <summary>
/// The AddTriagedSpecHashAndOpenWork migration adds nullable columns only: an issue stored before it reads as having no
/// triaged spec hash and no open work, in the state it was stored in. Runs locally against SQLite — no Docker required.
/// </summary>
public sealed class WhenAddTriagedSpecHashAndOpenWorkMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261124115922_AddRevisionAndContinuationBlockedStates";

    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenAddTriagedSpecHashAndOpenWorkMigrationRuns()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"triaged-spec-hash-migration-{Guid.NewGuid():N}.db");
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

    [Fact]
    public async Task AStoredTriageQueuedRow_ReadsAsHavingNoTriagedSpecHashAndNoOpenWork()
    {
        // Arrange
        Guid issueId = Guid.NewGuid();
        await InsertTriageQueuedRowAsync(issueId);

        // Act
        await _dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);

        // Assert
        Issue issue = await _dbContext.Set<Issue>()
            .SingleAsync(i => i.Id == IssueId.From(issueId), TestContext.Current.CancellationToken);
        issue.ShouldSatisfyAllConditions(
            () => issue.ShouldBeOfType<TriageQueuedIssue>(),
            () => issue.TriagedSpecHash.ShouldBeNull(),
            () => issue.OpenWork.ShouldBeNull());
    }

    private async Task InsertTriageQueuedRowAsync(Guid issueId)
    {
        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO issues " +
            "(id, monitored_repository_id, issue_number, title, author, url, labels, state, detected_at, entered_state_at) " +
            "VALUES ($id, $repoId, 7, 'Issue', 'octocat', 'https://github.com/o/r/issues/7', '[]', 'triage_queued', " +
            " '2026-11-20T10:00:00.0000000Z', '2026-11-20T10:00:00.0000000Z');";
        // EF Core stores a Guid in SQLite as uppercase text.
        command.Parameters.AddWithValue("$id", issueId.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$repoId", Guid.NewGuid().ToString().ToUpperInvariant());
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
