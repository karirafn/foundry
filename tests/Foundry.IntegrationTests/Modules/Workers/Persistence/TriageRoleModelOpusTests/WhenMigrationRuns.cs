using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Persistence.TriageRoleModelOpusTests;

/// <summary>Runs the real MoveTriageRoleModelToOpus migration: only the seeded Sonnet value moves to Opus, so an operator's choice stays.</summary>
public sealed class WhenMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261111180400_AddWorkerRunPublishing";
    private const string TargetMigrationId = "20261112120624_MoveTriageRoleModelToOpus";

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
    public async Task OnADatabaseWithTheSeededSonnetTriageModel_TheTriageRoleModelBecomesOpus()
    {
        // Arrange
        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);
        await ExecuteAsync("UPDATE worker_role_settings SET model = 'sonnet' WHERE role = 'triage';");

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await ScalarAsync("SELECT model FROM worker_role_settings WHERE role = 'triage';")).ShouldBe("opus");
    }

    [Theory]
    [InlineData("haiku")]
    [InlineData("opus[1m]")]
    public async Task OnADatabaseWithAnOperatorChosenTriageModel_KeepsIt(string model)
    {
        // Arrange
        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);
        await ExecuteAsync($"UPDATE worker_role_settings SET model = '{model}' WHERE role = 'triage';");

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await ScalarAsync("SELECT model FROM worker_role_settings WHERE role = 'triage';")).ShouldBe(model);
    }

    [Theory]
    [InlineData("opus", "sonnet")]
    [InlineData("haiku", "haiku")]
    public async Task WhenRolledBack_OnlyAnOpusTriageModelReturnsToSonnet(string modelAfterUp, string expectedAfterDown)
    {
        // Arrange
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);
        await ExecuteAsync($"UPDATE worker_role_settings SET model = '{modelAfterUp}' WHERE role = 'triage';");

        // Act
        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await ScalarAsync("SELECT model FROM worker_role_settings WHERE role = 'triage';")).ShouldBe(expectedAfterDown);
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
