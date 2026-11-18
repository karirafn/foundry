using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Persistence.ReviewRoleModelSeedTests;

/// <summary>Runs the real SeedReviewRoleModel migration: the review role starts on Opus.</summary>
public sealed class WhenMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261117172548_AddIssueWatchBranchHead";
    private const string TargetMigrationId = "20261118100613_SeedReviewRoleModel";

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
    public async Task OnAFreshDatabase_TheReviewRoleModelIsOpus()
    {
        // Act
        await _dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);

        // Assert
        (await ScalarAsync("SELECT model FROM worker_role_settings WHERE role = 'review';")).ShouldBe("opus");
    }

    [Fact]
    public async Task OnAnExistingDatabase_TheReviewRoleModelIsSeededAndTheOtherModelsAreKept()
    {
        // Arrange
        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);
        await ExecuteAsync("INSERT INTO worker_role_settings (role, model) VALUES ('implement', 'sonnet');");

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await ScalarAsync("SELECT model FROM worker_role_settings WHERE role = 'review';")).ShouldBe("opus");
        (await ScalarAsync("SELECT model FROM worker_role_settings WHERE role = 'implement';")).ShouldBe("sonnet");
        (await ScalarAsync("SELECT model FROM worker_role_settings WHERE role = 'triage';")).ShouldBe("opus");
    }

    [Fact]
    public async Task WhenTheReviewModelWasAlreadySet_KeepsIt()
    {
        // Arrange
        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);
        await ExecuteAsync("INSERT INTO worker_role_settings (role, model) VALUES ('review', 'sonnet');");

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await ScalarAsync("SELECT model FROM worker_role_settings WHERE role = 'review';")).ShouldBe("sonnet");
    }

    private async Task ExecuteAsync(string sql)
    {
#pragma warning disable CA2100 // Test-only constant SQL.
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string?> ScalarAsync(string sql)
    {
#pragma warning disable CA2100 // Test-only constant SQL.
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
#pragma warning restore CA2100
        return (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))?.ToString();
    }
}
