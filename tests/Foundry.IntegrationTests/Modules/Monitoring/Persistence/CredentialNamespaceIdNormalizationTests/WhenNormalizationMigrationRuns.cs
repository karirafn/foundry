using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Persistence.CredentialNamespaceIdNormalizationTests;

/// <summary>
/// Verifies AC1: the NormalizeCredentialNamespaceIds corrective migration normalizes
/// lowercase credential_namespaces.id values to uppercase (the format Microsoft.Data.Sqlite
/// emits when binding Guid parameters), and is idempotent on already-uppercase rows.
///
/// Also verifies AC3: after applying the fixed backfill migration from scratch, seeded ids
/// match Microsoft.Data.Sqlite's uppercase guid TEXT format.
/// </summary>
public sealed class WhenNormalizationMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string BackfillMigrationId = "20260718204901_AddCredentialHostAndNamespacesTable";
    private const string DropFkMigrationId = "20260718213128_DropMonitoredRepositoryAccountIdFk";
    private const string NormalizeMigrationId = "20260722225513_NormalizeCredentialNamespaceIds";

    // Uppercase UUID-v4 pattern as emitted by Microsoft.Data.Sqlite when binding a Guid parameter.
    private const string UppercaseGuidV4Pattern =
        @"^[0-9A-F]{8}-[0-9A-F]{4}-4[0-9A-F]{3}-[89AB][0-9A-F]{3}-[0-9A-F]{12}$";

    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenNormalizationMigrationRuns()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"normalize-ns-test-{Guid.NewGuid():N}.db");
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
    public async Task WhenIdsAreLowercase_CorrectiveMigrationNormalizesToUppercase()
    {
        // Arrange — bring schema to just before the corrective migration, then inject
        // lowercase ids directly (simulating what the original backfill SQL produced).
        await _dbContext.Database.MigrateAsync(DropFkMigrationId, TestContext.Current.CancellationToken);

        Guid credentialId = await InsertAccountAsync("https://github.com");
        Guid lowercaseId = Guid.NewGuid();
        await InsertNamespaceWithLowercaseIdAsync(lowercaseId, credentialId, "github.com", "acme");

        // Verify the row was inserted with a lowercase id.
        string rawId = await ReadRawIdAsync(lowercaseId);
        rawId.ShouldBe(lowercaseId.ToString("D").ToLowerInvariant());

        // Act — apply the corrective migration.
        await _dbContext.Database.MigrateAsync(NormalizeMigrationId, TestContext.Current.CancellationToken);

        // Assert — the id is now uppercase.
        string normalizedId = await ReadRawIdAsync(lowercaseId);
        normalizedId.ShouldBe(lowercaseId.ToString("D").ToUpperInvariant());
    }

    [Fact]
    public async Task WhenIdsAreAlreadyUppercase_CorrectiveMigrationIsIdempotent()
    {
        // Arrange — bring schema to just before the corrective migration, then inject
        // an already-uppercase id (simulating a hand-repaired live DB or an EF-written row).
        await _dbContext.Database.MigrateAsync(DropFkMigrationId, TestContext.Current.CancellationToken);

        Guid credentialId = await InsertAccountAsync("https://github.com");
        Guid uppercaseId = Guid.NewGuid();
        await InsertNamespaceWithUppercaseIdAsync(uppercaseId, credentialId, "github.com", "widgets");

        // Act — apply the corrective migration.
        await _dbContext.Database.MigrateAsync(NormalizeMigrationId, TestContext.Current.CancellationToken);

        // Assert — the already-uppercase id is unchanged (upper() of an uppercase string is a no-op).
        string idAfter = await ReadRawIdAsync(uppercaseId);
        idAfter.ShouldBe(uppercaseId.ToString("D").ToUpperInvariant());
    }

    [Fact]
    public async Task WhenFreshDatabaseMigrated_BackfillProducesUppercaseIds()
    {
        // Arrange — bring the schema to just before the backfill migration and seed a repo.
        const string priorMigration = "20260708052247_AddAccountBaseUrlNameUniqueIndex";
        await _dbContext.Database.MigrateAsync(priorMigration, TestContext.Current.CancellationToken);

        Guid accountId = await InsertAccountPreBackfillAsync("https://github.com");
        await InsertRepositoryPreBackfillAsync(accountId, "octocat/myrepo", "github.com");

        // Act — apply the backfill migration (which seeds credential_namespaces).
        await _dbContext.Database.MigrateAsync(BackfillMigrationId, TestContext.Current.CancellationToken);

        // Assert — the seeded id is uppercase and matches the UUID-v4 TEXT format that
        // Microsoft.Data.Sqlite emits when binding Guid parameters. This proves both that
        // all hex letters are uppercase and that the id is a well-formed Guid v4.
        List<string> rawIds = await LoadRawNamespaceIdsAsync();
        rawIds.Count.ShouldBe(1);

        string seededId = rawIds[0];
        seededId.ShouldMatch(
            UppercaseGuidV4Pattern,
            customMessage: "seeded id must be an uppercase UUID-v4 string matching Microsoft.Data.Sqlite's Guid binding format");
    }

    /// <summary>
    /// Inserts an account row after the host column exists (post-backfill schema).
    /// </summary>
    private async Task<Guid> InsertAccountAsync(string baseUrl)
    {
        Guid id = Guid.NewGuid();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO accounts (id, name, token, base_url, host, type) " +
            "VALUES ($id, $name, NULL, $baseUrl, $host, 'github');";
        command.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$name", $"account-{id:N}");
        command.Parameters.AddWithValue("$baseUrl", baseUrl);
        command.Parameters.AddWithValue("$host", new Uri(baseUrl).Host);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    /// <summary>
    /// Inserts an account row before the host column exists (pre-backfill schema).
    /// </summary>
    private async Task<Guid> InsertAccountPreBackfillAsync(string baseUrl)
    {
        Guid id = Guid.NewGuid();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO accounts (id, name, token, base_url, type) " +
            "VALUES ($id, $name, NULL, $baseUrl, 'github');";
        command.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$name", $"account-{id:N}");
        command.Parameters.AddWithValue("$baseUrl", baseUrl);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    /// <summary>
    /// Inserts a monitored_repository row before the account_id FK column is dropped.
    /// The position column is INTEGER — bind an integer, not a Guid string.
    /// </summary>
    private async Task InsertRepositoryPreBackfillAsync(Guid accountId, string slug, string host)
    {
        Guid id = Guid.NewGuid();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO monitored_repositories (id, account_id, slug, host, is_active, eligibility_status, position) " +
            "VALUES ($id, $accountId, $slug, $host, 1, 'unreachable', $position);";
        command.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$accountId", accountId.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$slug", slug);
        command.Parameters.AddWithValue("$host", host);
        command.Parameters.AddWithValue("$position", 0);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Inserts a credential_namespace row with a lowercase id to simulate the original
    /// buggy backfill SQL output.
    /// </summary>
    private async Task InsertNamespaceWithLowercaseIdAsync(
        Guid id,
        Guid credentialId,
        string host,
        string value)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO credential_namespaces (id, credential_id, host, value) " +
            "VALUES ($id, $credentialId, $host, $value);";
        command.Parameters.AddWithValue("$id", id.ToString("D").ToLowerInvariant());
        command.Parameters.AddWithValue("$credentialId", credentialId.ToString("D").ToUpperInvariant());
        command.Parameters.AddWithValue("$host", host);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Inserts a credential_namespace row with an uppercase id (EF-format).
    /// </summary>
    private async Task InsertNamespaceWithUppercaseIdAsync(
        Guid id,
        Guid credentialId,
        string host,
        string value)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO credential_namespaces (id, credential_id, host, value) " +
            "VALUES ($id, $credentialId, $host, $value);";
        command.Parameters.AddWithValue("$id", id.ToString("D").ToUpperInvariant());
        command.Parameters.AddWithValue("$credentialId", credentialId.ToString("D").ToUpperInvariant());
        command.Parameters.AddWithValue("$host", host);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Reads the raw TEXT id stored for a namespace row, looked up by the Guid value
    /// regardless of case (using SQLite's upper() for the lookup).
    /// Throws <see cref="InvalidOperationException"/> when no row is found for the given id,
    /// so callers get a clear failure message rather than an empty-string assertion.
    /// </summary>
    private async Task<string> ReadRawIdAsync(Guid id)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT id FROM credential_namespaces WHERE upper(id) = upper($id);";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        object? result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        if (result is null)
        {
            throw new InvalidOperationException($"Namespace row not found for id {id}");
        }

        return result.ToString()!;
    }

    private async Task<List<string>> LoadRawNamespaceIdsAsync()
    {
        List<string> results = [];
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT id FROM credential_namespaces;";
        using SqliteDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }
}
