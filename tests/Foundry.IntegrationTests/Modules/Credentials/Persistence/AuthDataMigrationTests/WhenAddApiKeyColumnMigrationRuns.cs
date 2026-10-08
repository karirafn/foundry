using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Persistence.AuthDataMigrationTests;

/// <summary>
/// Verifies the AddApiKeyColumn migration rewrites auth_mode from the old encrypted-blob form
/// to the new plaintext JSON discriminator, and adds the api_key column as NULL for all rows.
///
/// The previous migration (AddRepositoryMaxConcurrentWorkers) left claude_account rows with
/// auth_mode still containing whatever was seeded — typically an old encrypted blob or legacy
/// plaintext value. AddApiKeyColumn rewrites them via a SQL CASE on oauth_account_email:
///   - Row WITH oauth_account_email → {"type":"oauth"}
///   - Row WITHOUT oauth_account_email → {"type":"api_key"}
///
/// api_key is added as NULL for all existing rows.
/// These tests exercise raw SQL read-back only — no Data Protection decryption is required.
/// </summary>
public sealed class WhenAddApiKeyColumnMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261001182529_AddRepositoryMaxConcurrentWorkers";
    private const string TargetMigrationId = "20261008150254_AddApiKeyColumn";

    private const string OAuthRowId = "10000000-0000-0000-0000-000000000001";
    private const string ApiKeyRowId = "10000000-0000-0000-0000-000000000002";

    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenAddApiKeyColumnMigrationRuns()
    {
        _dbPath = Path.Combine(
            Path.GetTempPath(),
            $"migration-api-key-test-{Guid.NewGuid():N}.db");
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
            DataProtectionProvider.Create("api-key-column-migration-test"));

        // Migrate to the state immediately before AddApiKeyColumn — claude_account exists
        // but has no api_key column. auth_mode contains whatever was stored (may be a legacy
        // encrypted blob or any string value, since the migration rewrites it unconditionally).
        await _dbContext.Database.MigrateAsync(
            PreviousMigrationId,
            TestContext.Current.CancellationToken);
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
    public async Task WhenRowHasOAuthEmail_AuthModeBecomesOAuthJson()
    {
        // Arrange — seed a row with oauth_account_email set (simulates an OAuth account whose
        // auth_mode was stored as an old encrypted blob before this migration ran).
        await SeedClaudeAccountRowAsync(OAuthRowId, oauthEmail: "alice@example.com");

        // Act — apply AddApiKeyColumn, which rewrites auth_mode via SQL CASE.
        await _dbContext.Database.MigrateAsync(
            TargetMigrationId,
            TestContext.Current.CancellationToken);

        // Assert — auth_mode is now the plaintext JSON discriminator for OAuth.
        string? authMode = await ReadAuthModeAsync(OAuthRowId);
        authMode.ShouldBe(@"{""type"":""oauth""}");
    }

    [Fact]
    public async Task WhenRowHasNoOAuthEmail_AuthModeBecomesApiKeyJson()
    {
        // Arrange — seed a row without oauth_account_email (simulates an API-key account whose
        // auth_mode was stored as an old encrypted blob before this migration ran).
        await SeedClaudeAccountRowAsync(ApiKeyRowId, oauthEmail: null);

        // Act
        await _dbContext.Database.MigrateAsync(
            TargetMigrationId,
            TestContext.Current.CancellationToken);

        // Assert — auth_mode is now the plaintext JSON discriminator for API key.
        string? authMode = await ReadAuthModeAsync(ApiKeyRowId);
        authMode.ShouldBe(@"{""type"":""api_key""}");
    }

    [Fact]
    public async Task WhenMigrationRuns_ApiKeyColumnIsNull()
    {
        // Arrange — seed any row; the api_key column does not exist yet.
        await SeedClaudeAccountRowAsync(ApiKeyRowId, oauthEmail: null);

        // Act
        await _dbContext.Database.MigrateAsync(
            TargetMigrationId,
            TestContext.Current.CancellationToken);

        // Assert — api_key is NULL for all pre-existing rows (column added with no default).
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT api_key FROM claude_account WHERE id = $id;";
        command.Parameters.AddWithValue("$id", ApiKeyRowId);
        object? result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        result.ShouldBeOneOf(null, DBNull.Value);
    }

    [Fact]
    public async Task WhenMultipleRowsExist_BothAreRewritten()
    {
        // Arrange — seed one OAuth row and one API-key row.
        await SeedClaudeAccountRowAsync(OAuthRowId, oauthEmail: "bob@example.com");
        await SeedClaudeAccountRowAsync(ApiKeyRowId, oauthEmail: null);

        // Act
        await _dbContext.Database.MigrateAsync(
            TargetMigrationId,
            TestContext.Current.CancellationToken);

        // Assert — both rows are rewritten to the correct discriminator.
        string? oauthMode = await ReadAuthModeAsync(OAuthRowId);
        string? apiKeyMode = await ReadAuthModeAsync(ApiKeyRowId);
        oauthMode.ShouldBe(@"{""type"":""oauth""}");
        apiKeyMode.ShouldBe(@"{""type"":""api_key""}");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a claude_account row via raw ADO.NET using only the columns that exist after
    /// the PreviousMigrationId (i.e. before api_key was added). auth_mode is set to a sentinel
    /// value that the migration will overwrite — the exact content is irrelevant because the
    /// migration's CASE rewrites it unconditionally based on oauth_account_email.
    /// </summary>
    private async Task SeedClaudeAccountRowAsync(string id, string? oauthEmail)
    {
        string now = "2026-01-01T00:00:00.0000000+00:00";

        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO claude_account (" +
            "    id, auth_mode, oauth_account_email, oauth_account_org_name," +
            "    spend_state, validity, created_at, updated_at" +
            ") VALUES (" +
            "    $id, $authMode, $oauthEmail, NULL," +
            "    $spendState, $validity, $now, $now" +
            ");";

        command.Parameters.AddWithValue("$id", id);
        // Sentinel: any string that is not valid plaintext JSON — simulates an old encrypted blob.
        command.Parameters.AddWithValue("$authMode", "sentinel-old-encrypted-blob");
        command.Parameters.AddWithValue("$oauthEmail", oauthEmail is null ? DBNull.Value : oauthEmail);
        command.Parameters.AddWithValue("$spendState", @"{""type"":""available""}");
        command.Parameters.AddWithValue("$validity", @"{""type"":""valid""}");
        command.Parameters.AddWithValue("$now", now);

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string?> ReadAuthModeAsync(string id)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT auth_mode FROM claude_account WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        object? result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return result as string;
    }
}
