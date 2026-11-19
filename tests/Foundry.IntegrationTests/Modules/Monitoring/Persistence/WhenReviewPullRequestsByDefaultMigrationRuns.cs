using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Persistence;

public sealed class WhenReviewPullRequestsByDefaultMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261119175732_AddWebhookSpecStamp";
    private const string TargetMigrationId = "20261119203127_ReviewPullRequestsByDefault";

    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenReviewPullRequestsByDefaultMigrationRuns()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"review-by-default-migration-{Guid.NewGuid():N}.db");
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

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task ExistingRepositories_ReviewPullRequestsBecomesTrue()
    {
        // Arrange
        await InsertRepositoryAsync("owner/repo-a", position: 0);
        await InsertRepositoryAsync("owner/repo-b", position: 1);

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        List<long> values = await ReadReviewPullRequestsAsync();
        values.Count.ShouldBe(2);
        values.ShouldAllBe(v => v == 1);
    }

    [Fact]
    public async Task RepositoryInsertedWithoutTheColumn_ReviewsPullRequests()
    {
        // Arrange
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Act
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO monitored_repositories (id, slug, host, is_active, position, apply_branch_rules, webhook_hook_id) " +
                "VALUES ($id, 'owner/repo-c', 'github.com', 1, 0, 0, $hookId);";
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("$hookId", Guid.NewGuid().ToString("N"));
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        (await ReadReviewPullRequestsAsync()).ShouldHaveSingleItem().ShouldBe(1);
    }

    private async Task<List<long>> ReadReviewPullRequestsAsync()
    {
        List<long> values = [];
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT review_pull_requests FROM monitored_repositories;";
        using SqliteDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetInt64(0));
        }

        return values;
    }

    private async Task InsertRepositoryAsync(string slug, int position)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO monitored_repositories " +
            "(id, slug, host, is_active, position, apply_branch_rules, webhook_hook_id, review_pull_requests) " +
            "VALUES ($id, $slug, 'github.com', 1, $position, 0, $hookId, 0);";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$slug", slug);
        command.Parameters.AddWithValue("$position", position);
        command.Parameters.AddWithValue("$hookId", Guid.NewGuid().ToString("N"));
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
