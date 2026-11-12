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
    private const string TriageRole = "triage";
    private const string OtherRole = "implement";
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
        await SetTriageModelAsync("sonnet");

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await ScalarAsync("SELECT model FROM worker_role_settings WHERE role = 'triage';")).ShouldBe("opus");
    }

    [Theory]
    [InlineData("")]
    [InlineData("haiku")]
    [InlineData("opus[1m]")]
    public async Task OnADatabaseWithAnOperatorChosenTriageModel_KeepsIt(string model)
    {
        // Arrange
        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);
        await SetTriageModelAsync(model);

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await ScalarAsync("SELECT model FROM worker_role_settings WHERE role = 'triage';")).ShouldBe(model);
    }

    [Theory]
    [InlineData("opus", "sonnet")]
    [InlineData("haiku", "haiku")]
    public async Task OnRollback_OnlyAnOpusTriageModelReturnsToSonnet(string modelAfterUp, string expectedAfterDown)
    {
        // Arrange
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);
        await SetTriageModelAsync(modelAfterUp);

        // Act
        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await ScalarAsync("SELECT model FROM worker_role_settings WHERE role = 'triage';")).ShouldBe(expectedAfterDown);
    }

    [Fact]
    public async Task OnADatabaseWithAnotherRoleOnSonnet_KeepsIt()
    {
        // Arrange
        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);
        await SetModelAsync(OtherRole, "sonnet");

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await ScalarAsync($"SELECT model FROM worker_role_settings WHERE role = '{OtherRole}';")).ShouldBe("sonnet");
    }

    [Fact]
    public async Task OnRollback_AnotherRoleOnOpusKeepsIt()
    {
        // Arrange
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);
        await SetModelAsync(OtherRole, "opus");

        // Act
        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await ScalarAsync($"SELECT model FROM worker_role_settings WHERE role = '{OtherRole}';")).ShouldBe("opus");
    }

    private Task SetTriageModelAsync(string model) => SetModelAsync(TriageRole, model);

    private async Task SetModelAsync(string role, string model)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "INSERT INTO worker_role_settings (role, model) VALUES ($role, $model) ON CONFLICT(role) DO UPDATE SET model = excluded.model;";
        command.Parameters.AddWithValue("$model", model);
        command.Parameters.AddWithValue("$role", role);
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
