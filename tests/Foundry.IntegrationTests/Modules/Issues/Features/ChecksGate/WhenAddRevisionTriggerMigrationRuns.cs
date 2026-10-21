using System.Globalization;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Infrastructure.Configurations;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Features.ChecksGate;

/// <summary>
/// Verifies the AddRevisionTrigger migration Up() backfill SQL by running the actual EF migration.
/// Seeds a pre-migration row with PascalCase review_comments JSON (as ReviewCommentsJsonConversion
/// serialised it) and asserts that after the migration the entity materialises via RevisionTriggerJsonConversion
/// with a ReviewFeedback trigger whose comments, omitted count, and newest-at survive intact.
/// Runs locally against SQLite — no Docker required.
/// </summary>
public sealed class WhenAddRevisionTriggerMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261021144120_AddChecksPendingState";
    private const string TargetMigrationId = "20261021180338_AddRevisionTrigger";

    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenAddRevisionTriggerMigrationRuns()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"revision-trigger-migration-{Guid.NewGuid():N}.db");
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

        // Bring the schema up to AddChecksPendingState — the migration immediately before
        // AddRevisionTrigger. At this point review_comments / omitted_comment_count /
        // newest_consumed_comment_at exist and revision_trigger does not.
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
    public async Task RevisionQueuedRowWithPascalCaseReviewComments_MaterialisesCorrectlyAfterMigration()
    {
        // Arrange — insert a revision_queued row at the pre-migration schema.
        // review_comments is PascalCase JSON as ReviewCommentsJsonConversion (default STJ options) wrote it.
        // The omitted_comment_count and newest_consumed_comment_at columns are also populated.
        Guid issueId = Guid.NewGuid();
        Guid repositoryId = Guid.NewGuid();
        Guid workerRunId = Guid.NewGuid();
        DateTimeOffset newestAt = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

        await InsertRevisionQueuedRowAsync(issueId, repositoryId, workerRunId, newestAt);

        // Act — apply AddRevisionTrigger, which runs the backfill SQL.
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — the row materialises as a RevisionQueuedIssue with the correct ReviewFeedback trigger.
        // Look up by IssueNumber rather than id: raw SQL inserts the id in lowercase hyphenated GUID format,
        // but EF + Microsoft.Data.Sqlite emits the id parameter in uppercase canonical GUID format, so the
        // WHERE-clause string comparison on the id column finds nothing and returns null.
        _dbContext.ChangeTracker.Clear();
        Issue? loaded = await _dbContext
            .Set<Issue>()
            .FirstOrDefaultAsync(i => i.IssueNumber == 10, TestContext.Current.CancellationToken);

        RevisionQueuedIssue revisionQueued = loaded.ShouldBeOfType<RevisionQueuedIssue>();
        RevisionTrigger.ReviewFeedback rf = revisionQueued.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFeedback>();

        revisionQueued.ShouldSatisfyAllConditions(
            () => rf.OmittedCommentCount.ShouldBe(3),
            () => rf.NewestConsumedCommentAt.ShouldBe(newestAt),
            () => rf.Comments.Count.ShouldBe(2),
            () => rf.Comments[0].Body.ShouldBe("Please fix the formatting."),
            () => rf.Comments[0].FilePath.ShouldBeNull(),
            () => rf.Comments[0].Line.ShouldBeNull(),
            () => rf.Comments[1].Body.ShouldBe("Rename this variable."),
            () => rf.Comments[1].FilePath.ShouldBe("src/Foo.cs"),
            () => rf.Comments[1].Line.ShouldBe(42));
    }

    [Fact]
    public async Task NonRevisionRow_IsNotAffectedByMigration()
    {
        // Arrange — insert a detected-state issue (no revision fields).
        Guid issueId = Guid.NewGuid();
        Guid repositoryId = Guid.NewGuid();
        await InsertDetectedIssueAsync(issueId, repositoryId);

        // Act — apply AddRevisionTrigger.
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — non-revision row is untouched (revision_trigger should be NULL).
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT state, revision_trigger FROM issues WHERE id = $id;";
        command.Parameters.AddWithValue("$id", issueId.ToString());
        using SqliteDataReader reader =
            await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);
        string state = reader.GetString(0);
        bool revisionTriggerIsNull = await reader.IsDBNullAsync(1, TestContext.Current.CancellationToken);

        state.ShouldBe("detected");
        revisionTriggerIsNull.ShouldBeTrue();
    }

    [Fact]
    public async Task RevisionQueuedRowWithNullNewestConsumedCommentAt_MaterialisesCorrectlyAfterMigration()
    {
        // Arrange — insert a revision_queued row with null newest_consumed_comment_at.
        Guid issueId = Guid.NewGuid();
        Guid repositoryId = Guid.NewGuid();
        Guid workerRunId = Guid.NewGuid();

        await InsertRevisionQueuedRowWithNullNewestAtAsync(issueId, repositoryId, workerRunId);

        // Act — apply AddRevisionTrigger.
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — the row materialises with NewestConsumedCommentAt == null.
        _dbContext.ChangeTracker.Clear();
        Issue? loaded = await _dbContext
            .Set<Issue>()
            .FirstOrDefaultAsync(i => i.IssueNumber == 12, TestContext.Current.CancellationToken);

        RevisionQueuedIssue revisionQueued = loaded.ShouldBeOfType<RevisionQueuedIssue>();
        RevisionTrigger.ReviewFeedback rf = revisionQueued.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFeedback>();

        rf.NewestConsumedCommentAt.ShouldBeNull();
    }

    private async Task InsertRevisionQueuedRowAsync(
        Guid issueId,
        Guid repositoryId,
        Guid workerRunId,
        DateTimeOffset newestAt)
    {
        // PascalCase JSON — exactly as ReviewCommentsJsonConversion (default STJ options) produced it.
        string reviewCommentsJson =
            """[{"Body":"Please fix the formatting.","FilePath":null,"Line":null},{"Body":"Rename this variable.","FilePath":"src/Foo.cs","Line":42}]""";

        // The pre-migration schema has: review_comments, omitted_comment_count, newest_consumed_comment_at.
        // The check constraint (ck_issues_revision_queued_fields) is not yet present at this schema point,
        // so we can insert freely.
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO issues " +
            "(id, monitored_repository_id, issue_number, title, author, url, labels, state, detected_at, " +
            " branch_name, pull_request_url, feedback_cutoff_at, review_comments, omitted_comment_count, " +
            " newest_consumed_comment_at) " +
            "VALUES ($id, $repoId, 10, 'Revision issue', 'octocat', 'https://github.com/o/r/issues/10', " +
            " '[]', 'revision_queued', datetime('now'), 'feat/10-test', " +
            " 'https://github.com/o/r/pull/10', datetime('now', '+1 day'), " +
            " $reviewComments, 3, $newestAt);";
        command.Parameters.AddWithValue("$id", issueId.ToString());
        command.Parameters.AddWithValue("$repoId", repositoryId.ToString());
        command.Parameters.AddWithValue("$reviewComments", reviewCommentsJson);
        // Seed in EF+Microsoft.Data.Sqlite's actual storage format for DateTimeOffset?: space separator,
        // no 'T' between date and time (e.g. "2026-09-15 12:00:00.0000000+00:00"). Using the "O" / ISO-T
        // form would mask the bug because STJ's DateTimeOffset converter already accepts 'T'-form input.
        command.Parameters.AddWithValue("$newestAt", newestAt.ToString("yyyy-MM-dd HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task InsertRevisionQueuedRowWithNullNewestAtAsync(
        Guid issueId,
        Guid repositoryId,
        Guid workerRunId)
    {
        string reviewCommentsJson =
            """[{"Body":"Fix this.","FilePath":null,"Line":null}]""";

        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO issues " +
            "(id, monitored_repository_id, issue_number, title, author, url, labels, state, detected_at, " +
            " branch_name, pull_request_url, feedback_cutoff_at, review_comments, omitted_comment_count, " +
            " newest_consumed_comment_at) " +
            "VALUES ($id, $repoId, 12, 'Null newest-at issue', 'octocat', 'https://github.com/o/r/issues/12', " +
            " '[]', 'revision_queued', datetime('now'), 'feat/12-test', " +
            " 'https://github.com/o/r/pull/12', datetime('now', '+1 day'), " +
            " $reviewComments, 0, NULL);";
        command.Parameters.AddWithValue("$id", issueId.ToString());
        command.Parameters.AddWithValue("$repoId", repositoryId.ToString());
        command.Parameters.AddWithValue("$reviewComments", reviewCommentsJson);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task InsertDetectedIssueAsync(Guid issueId, Guid repositoryId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO issues " +
            "(id, monitored_repository_id, issue_number, title, author, url, labels, state, detected_at) " +
            "VALUES ($id, $repoId, 11, 'Detected issue', 'octocat', 'https://github.com/o/r/issues/11', " +
            " '[]', 'detected', datetime('now'));";
        command.Parameters.AddWithValue("$id", issueId.ToString());
        command.Parameters.AddWithValue("$repoId", repositoryId.ToString());
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
