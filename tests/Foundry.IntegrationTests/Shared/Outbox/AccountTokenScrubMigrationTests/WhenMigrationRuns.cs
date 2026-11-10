using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Shared.Outbox.AccountTokenScrubMigrationTests;

/// <summary>
/// Runs the real ScrubAccountTokenFromIssueClaimedOutbox migration against outbox rows seeded through raw ADO.NET:
/// the plaintext account token is removed from every IssueClaimed payload and nothing else changes.
/// </summary>
public sealed class WhenMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261109131829_PersistCompletedRunContainerOutput";
    private const string TargetMigrationId = "20261110124411_ScrubAccountTokenFromIssueClaimedOutbox";

    private const string IssueClaimedType =
        "Foundry.Modules.Issues.Contracts.IssueClaimed, Foundry.Modules.Issues.Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

    private const string OtherEventType =
        "Foundry.Modules.Issues.Contracts.IssueQueued, Foundry.Modules.Issues.Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

    private const string PayloadPrefix =
        """{"Dispatch":{"IssueId":{"Value":"0b1e0f0c-5a3e-4d7c-9a51-2f1f6f1c2d3e"},"IssueNumber":7,"Title":"Fix it","CloneUrl":"https://github.com/org/repo.git",""";

    private const string PayloadSuffix =
        "\"" + """BranchName":{"Value":"feat/7-fix"},"Provider":{"$type":"github"},"IssueApiUrl":"https://api.github.com/repos/org/repo/issues/7"}}""";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"migration-test-{Guid.NewGuid():N}.db");
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

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
    [InlineData("2026-11-01T00:00:00.0000000+00:00")]
    [InlineData(null)]
    public async Task AnIssueClaimedRowsAccountToken_IsRemovedAndTheRestOfThePayloadKept(string? processedAt)
    {
        // Arrange — published and still-unpublished rows alike.
        Guid id = await InsertOutboxRowAsync(
            IssueClaimedType,
            PayloadPrefix + "\"AccountToken\":\"ghp_plaintext_secret\"," + PayloadSuffix,
            processedAt);

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await PayloadAsync(id)).ShouldBe(PayloadPrefix + PayloadSuffix);
    }

    [Fact]
    public async Task OtherEventTypes_AreLeftUntouched()
    {
        // Arrange
        string payload = """{"AccountToken":"not-an-issue-claimed-row","IssueNumber":3}""";
        Guid id = await InsertOutboxRowAsync(OtherEventType, payload, processedAt: null);

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await PayloadAsync(id)).ShouldBe(payload);
    }

    private async Task<Guid> InsertOutboxRowAsync(string type, string payload, string? processedAt)
    {
        Guid id = Guid.NewGuid();

        using SqliteCommand insert = _connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO outbox_messages (id, type, payload, occurred_at, attempts, processed_at, error) "
            + "VALUES ($id, $type, $payload, '2026-11-01T00:00:00.0000000+00:00', 0, $processedAt, NULL);";
        insert.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
        insert.Parameters.AddWithValue("$type", type);
        insert.Parameters.AddWithValue("$payload", payload);
        insert.Parameters.AddWithValue("$processedAt", (object?)processedAt ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        return id;
    }

    private async Task<string?> PayloadAsync(Guid id)
    {
        using SqliteCommand select = _connection.CreateCommand();
        select.CommandText = "SELECT payload FROM outbox_messages WHERE id = $id;";
        select.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
        object? value = await select.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value as string;
    }
}
