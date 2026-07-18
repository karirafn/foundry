using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Persistence.CredentialNamespaceMigrationTests;

/// <summary>
/// Verifies that the AddCredentialHostAndNamespacesTable migration Up() correctly seeds
/// credential_namespaces from monitored_repositories, and that the seeded namespaces
/// allow credential resolution via ResolveCoveringNamespace.
/// </summary>
public sealed class WhenMigrationSeeds : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20260708052247_AddAccountBaseUrlNameUniqueIndex";
    private const string TargetMigrationId = "20260718204901_AddCredentialHostAndNamespacesTable";

    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenMigrationSeeds()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"migration-seed-test-{Guid.NewGuid():N}.db");
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

        // Bring the schema to the state just before the new migration.
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
    public async Task SingleSegmentOwner_SeedsFirstSegmentAsNamespace()
    {
        // Arrange — "octocat" is a single-segment owner; first segment is "octocat" itself.
        Guid accountId = await InsertAccountAsync("https://github.com");
        await InsertRepositoryAsync(accountId, "octocat/myproject", "github.com");

        // Act — apply the migration
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — the seeded namespace value is the first segment of the slug owner
        List<(Guid CredentialId, string Host, string Value)> rows = await LoadNamespacesAsync();

        rows.Count.ShouldBe(1);
        rows[0].ShouldSatisfyAllConditions(
            () => rows[0].CredentialId.ShouldBe(accountId),
            () => rows[0].Host.ShouldBe("github.com"),
            () => rows[0].Value.ShouldBe("octocat"));
    }

    [Fact]
    public async Task MultiSegmentOwner_SeedsFullOwnerAsNamespace()
    {
        // Arrange — "efla/databridge" is the full owner of slug "efla/databridge/akraborg.databridge".
        // The migration must seed the full owner (everything before the LAST '/'), matching
        // NamespaceDerivation.FromWritableRepositories which uses slug[..lastSlash].
        Guid accountId = await InsertAccountAsync("https://gitlab.example.com");
        await InsertRepositoryAsync(accountId, "efla/databridge/akraborg.databridge", "gitlab.example.com");

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — namespace is the FULL owner "efla/databridge", not just "efla"
        List<(Guid CredentialId, string Host, string Value)> rows = await LoadNamespacesAsync();

        rows.Count.ShouldBe(1);
        rows[0].Value.ShouldBe("efla/databridge");
    }

    [Fact]
    public async Task MultipleReposWithSameOwner_SeedsSingleNamespaceRow()
    {
        // Arrange — two repos with the same first-segment owner seed a single de-duplicated row.
        Guid accountId = await InsertAccountAsync("https://github.com");
        await InsertRepositoryAsync(accountId, "myorg/repo-a", "github.com");
        await InsertRepositoryAsync(accountId, "myorg/repo-b", "github.com");

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — only one namespace row for "myorg"
        List<(Guid CredentialId, string Host, string Value)> rows = await LoadNamespacesAsync();

        rows.Count.ShouldBe(1);
        rows[0].Value.ShouldBe("myorg");
    }

    [Fact]
    public async Task HostBackfill_ExtractsNormalizedHostFromBaseUrl()
    {
        // Arrange — insert an account with a standard GitLab self-hosted URL.
        Guid accountId = await InsertAccountAsync("https://gitlab.corp.example.com");

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — the host column is correctly extracted from the base_url
        string host = await LoadAccountHostAsync(accountId);
        host.ShouldBe("gitlab.corp.example.com");
    }

    [Fact]
    public async Task HostBackfill_WithPort_StripsPortAndLowercases()
    {
        // Arrange — self-hosted URL with a port number (e.g. Nexus/GitLab on a non-standard port).
        // Runtime behaviour: Credential.Host = baseUrl.Value.Host (System.Uri.Host), which is
        // lowercased and port-stripped. The migration must produce the same value so that
        // CredentialResolver (which filters on c.Host == host) can match post-migration rows.
        Guid accountId = await InsertAccountAsync("https://gitlab.corp.example.com:8443");

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — host is "gitlab.corp.example.com" (no port, lowercased)
        string host = await LoadAccountHostAsync(accountId);
        host.ShouldBe("gitlab.corp.example.com");
    }

    [Fact]
    public async Task TwoCredentials_WithDistinctSubGroupOwners_SeedDistinctNamespaces()
    {
        // Regression test for Finding 2: before the fix, the seed extracted only the FIRST segment
        // of the owner path ("efla" from both "efla/databridge" and "efla/other"). With INSERT OR IGNORE
        // on unique(host, namespace), the second credential's row was silently dropped — so repos under
        // "efla/other/*" would mis-route to credential A, which cannot push there (the 403 incident class).
        //
        // After the fix, the full owner is seeded so "efla/databridge" and "efla/other" are distinct rows,
        // and each credential resolves independently.
        Guid credentialAId = await InsertAccountAsync("https://gitlab.example.com");
        Guid credentialBId = await InsertAccountAsync("https://gitlab.example.com");

        await InsertRepositoryAsync(credentialAId, "efla/databridge/akraborg.databridge", "gitlab.example.com");
        await InsertRepositoryAsync(credentialBId, "efla/other/myrepo", "gitlab.example.com");

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — two distinct namespace rows, one per credential
        List<(Guid CredentialId, string Host, string Value)> rows = await LoadNamespacesAsync();

        rows.Count.ShouldBe(2);
        rows.ShouldContain(r => r.CredentialId == credentialAId && r.Value == "efla/databridge");
        rows.ShouldContain(r => r.CredentialId == credentialBId && r.Value == "efla/other");
    }

    [Fact]
    public async Task SeededNamespace_AllowsResolutionViaResolveCoveringNamespace()
    {
        // Arrange — seed a repo with owner "acme"; migration creates namespace "acme".
        Guid accountId = await InsertAccountAsync("https://github.com");
        await InsertRepositoryAsync(accountId, "acme/platform", "github.com");

        // Act — apply the migration
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert — the credential loaded with its namespaces resolves "acme/platform"
        // through ResolveCoveringNamespace to the seeded "acme" namespace.
        // Load all credentials with their namespaces, then find the one seeded for this test.
        // The filtered EF query using CredentialId comparison may not translate in all
        // DbContext configurations, so we load all and filter in memory here.
        List<GitHubCredential> allCredentials = await _dbContext.Set<Credential>()
            .OfType<GitHubCredential>()
            .Include(c => c.Namespaces)
            .ToListAsync(TestContext.Current.CancellationToken);

        GitHubCredential? credential = allCredentials
            .FirstOrDefault(c => c.Id.Value == accountId);

        credential.ShouldNotBeNull();
        credential.Namespaces.Count.ShouldBe(1);

        RepositorySlug slug = RepositorySlug.Create("acme/platform").ValueOrThrow();
        Namespace? resolved = credential.ResolveCoveringNamespace(slug);

        resolved.ShouldNotBeNull();
        resolved.Value.ShouldBe("acme");
    }

    /// <summary>
    /// Inserts an account row via raw ADO.NET using only the pre-migration columns
    /// (no host — the column does not exist yet at this migration level).
    /// </summary>
    private async Task<Guid> InsertAccountAsync(string baseUrl)
    {
        Guid id = Guid.NewGuid();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO accounts (id, name, token, base_url, type) " +
            "VALUES ($id, $name, NULL, $baseUrl, 'github');";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$name", $"account-{id:N}");
        command.Parameters.AddWithValue("$baseUrl", baseUrl);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    /// <summary>
    /// Inserts a monitored_repository row via raw ADO.NET using only the pre-migration columns.
    /// </summary>
    private async Task InsertRepositoryAsync(Guid accountId, string slug, string host)
    {
        Guid id = Guid.NewGuid();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO monitored_repositories (id, account_id, slug, host, is_active, eligibility_status, position) " +
            "VALUES ($id, $accountId, $slug, $host, 1, 'unreachable', $position);";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$accountId", accountId.ToString());
        command.Parameters.AddWithValue("$slug", slug);
        command.Parameters.AddWithValue("$host", host);
        command.Parameters.AddWithValue("$position", id.ToString());
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<(Guid CredentialId, string Host, string Value)>> LoadNamespacesAsync()
    {
        List<(Guid, string, string)> results = [];
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT credential_id, host, value FROM credential_namespaces ORDER BY value;";
        using SqliteDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            Guid credentialId = Guid.Parse(reader.GetString(0));
            string host = reader.GetString(1);
            string value = reader.GetString(2);
            results.Add((credentialId, host, value));
        }

        return results;
    }

    private async Task<string> LoadAccountHostAsync(Guid accountId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT host FROM accounts WHERE id = $id;";
        command.Parameters.AddWithValue("$id", accountId.ToString());
        object? result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return result?.ToString() ?? string.Empty;
    }
}
