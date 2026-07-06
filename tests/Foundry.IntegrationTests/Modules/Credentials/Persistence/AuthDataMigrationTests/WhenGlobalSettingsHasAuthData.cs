using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Persistence.AuthDataMigrationTests;

/// <summary>
/// Verifies the AddClaudeAccountTable migration copies auth data from global_settings
/// to claude_account and drops the auth columns from global_settings.
/// </summary>
public sealed class WhenGlobalSettingsHasAuthData : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20260703113029_FixWorkerRunTotalCostUsdPrecision";
    private const string TargetMigrationId = "20260706010232_AddClaudeAccountTable";
    private const string DefaultClaudeAccountId = "00000000-0000-0000-0000-000000000002";
    private const string GlobalSettingsId = "00000000-0000-0000-0000-000000000001";

    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenGlobalSettingsHasAuthData()
    {
        _dbPath = Path.Combine(
            Path.GetTempPath(),
            $"migration-auth-test-{Guid.NewGuid():N}.db");
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
    public async Task AuthMode_IscopiedFromGlobalSettingsToClaudeAccount()
    {
        // Arrange — insert a global_settings row with a known auth_mode ciphertext value.
        string authModeCiphertext = await InsertGlobalSettingsRowAsync(authInvalidPause: false);

        // Act — apply the AddClaudeAccountTable migration.
        await _dbContext.Database.MigrateAsync(
            TargetMigrationId,
            TestContext.Current.CancellationToken);

        // Assert — claude_account row has the same auth_mode ciphertext.
        string? copiedAuthMode = await ReadClaudeAccountAuthModeAsync();
        copiedAuthMode.ShouldBe(authModeCiphertext);
    }

    [Fact]
    public async Task OAuthAccountEmail_IsCopiedFromGlobalSettingsToClaudeAccount()
    {
        // Arrange
        await InsertGlobalSettingsRowAsync(
            authInvalidPause: false,
            oauthEmail: "user@example.com",
            oauthOrgName: "ExampleOrg");

        // Act
        await _dbContext.Database.MigrateAsync(
            TargetMigrationId,
            TestContext.Current.CancellationToken);

        // Assert
        (string? email, string? orgName) = await ReadClaudeAccountOAuthIdentityAsync();
        email.ShouldBe("user@example.com");
        orgName.ShouldBe("ExampleOrg");
    }

    [Fact]
    public async Task WhenAuthInvalidPause_IsTrue_ValidityIsSetToInvalid()
    {
        // Arrange
        await InsertGlobalSettingsRowAsync(authInvalidPause: true);

        // Act
        await _dbContext.Database.MigrateAsync(
            TargetMigrationId,
            TestContext.Current.CancellationToken);

        // Assert
        string? validity = await ReadClaudeAccountValidityAsync();
        string nonNull = validity.ShouldNotBeNull();
        nonNull.ShouldContain(@"""type"":""invalid""");
        nonNull.ShouldContain("migrated_from_global_settings");
    }

    [Fact]
    public async Task WhenAuthInvalidPause_IsFalse_ValidityIsSetToValid()
    {
        // Arrange
        await InsertGlobalSettingsRowAsync(authInvalidPause: false);

        // Act
        await _dbContext.Database.MigrateAsync(
            TargetMigrationId,
            TestContext.Current.CancellationToken);

        // Assert
        string? validity = await ReadClaudeAccountValidityAsync();
        string nonNull = validity.ShouldNotBeNull();
        nonNull.ShouldContain(@"""type"":""valid""");
    }

    [Fact]
    public async Task GlobalSettings_NoLongerHasAuthColumns()
    {
        // Arrange
        await InsertGlobalSettingsRowAsync(authInvalidPause: false);

        // Act
        await _dbContext.Database.MigrateAsync(
            TargetMigrationId,
            TestContext.Current.CancellationToken);

        // Assert — querying the dropped columns should fail with SQLite schema error.
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT auth_mode FROM global_settings LIMIT 1;";
        await Should.ThrowAsync<SqliteException>(
            async () => await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Inserts a global_settings row via raw ADO.NET using only the pre-migration columns.
    /// Returns the raw auth_mode value that was inserted.
    /// </summary>
    private async Task<string> InsertGlobalSettingsRowAsync(
        bool authInvalidPause,
        string? oauthEmail = null,
        string? oauthOrgName = null)
    {
        // Use a recognisable sentinel value — the migration copies ciphertext verbatim
        // (same Data Protection purpose), so we verify the raw bytes are preserved.
        string authModeCiphertext = "sentinel-auth-mode-ciphertext";
        string imageBuildStatus = @"{""type"":""idle""}";
        string workerImageConfig = @"{""baseImage"":""ghcr.io/anthropics/claude-code:latest"",""buildArgs"":[],""mounts"":[]}";
        string now = "2026-01-01 00:00:00.0000000+00:00";

        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO global_settings (" +
            "    id, auth_mode, auth_invalid_pause, oauth_account_email, oauth_account_org_name," +
            "    max_concurrent, timeout_minutes, auto_resume_on_usage_reset, default_cooldown_minutes," +
            "    is_dispatch_paused, image_build_status, worker_image_configuration," +
            "    created_at, updated_at" +
            ") VALUES (" +
            "    $id, $authMode, $authInvalidPause, $oauthEmail, $oauthOrgName," +
            "    1, 120, 1, 60," +
            "    0, $imageBuildStatus, $workerImageConfig," +
            "    $now, $now" +
            ");";

        command.Parameters.AddWithValue("$id", GlobalSettingsId);
        command.Parameters.AddWithValue("$authMode", authModeCiphertext);
        command.Parameters.AddWithValue("$authInvalidPause", authInvalidPause ? 1 : 0);
        command.Parameters.AddWithValue("$oauthEmail", oauthEmail is null ? DBNull.Value : oauthEmail);
        command.Parameters.AddWithValue("$oauthOrgName", oauthOrgName is null ? DBNull.Value : oauthOrgName);
        command.Parameters.AddWithValue("$imageBuildStatus", imageBuildStatus);
        command.Parameters.AddWithValue("$workerImageConfig", workerImageConfig);
        command.Parameters.AddWithValue("$now", now);

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return authModeCiphertext;
    }

    private async Task<string?> ReadClaudeAccountAuthModeAsync()
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "SELECT auth_mode FROM claude_account WHERE id = $id;";
        command.Parameters.AddWithValue("$id", DefaultClaudeAccountId);
        object? result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return result as string;
    }

    private async Task<(string? Email, string? OrgName)> ReadClaudeAccountOAuthIdentityAsync()
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "SELECT oauth_account_email, oauth_account_org_name FROM claude_account WHERE id = $id;";
        command.Parameters.AddWithValue("$id", DefaultClaudeAccountId);

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        if (!await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            return (null, null);
        }

        string? email = await reader.IsDBNullAsync(0, TestContext.Current.CancellationToken)
            ? null
            : reader.GetString(0);
        string? orgName = await reader.IsDBNullAsync(1, TestContext.Current.CancellationToken)
            ? null
            : reader.GetString(1);
        return (email, orgName);
    }

    private async Task<string?> ReadClaudeAccountValidityAsync()
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "SELECT validity FROM claude_account WHERE id = $id;";
        command.Parameters.AddWithValue("$id", DefaultClaudeAccountId);
        object? result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return result as string;
    }
}
