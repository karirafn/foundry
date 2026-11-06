using System.Text.Json;

using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.WorkerEnvironmentMigrationTests;

/// <summary>
/// Runs the real AddWorkerResourceLimitsAndMounts migration Up() against a pre-upgrade
/// global_settings row, proving the column defaults backfill existing rows and load as the
/// current worker environment defaults.
/// </summary>
public sealed class WhenMigrationRunsOnExistingGlobalSettings : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261030193511_AddWorkerImageTagAndBuildToggle";
    private const string TargetMigrationId = "20261102083801_AddWorkerResourceLimitsAndMounts";
    private const string SelectLimitsSql = "SELECT worker_resource_limits FROM global_settings;";

    private const int FrozenMemoryLimitMb = 8192;
    private const double FrozenCpuLimit = 2.0;
    private const int FrozenPidsLimit = 512;

    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenMigrationRunsOnExistingGlobalSettings()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"migration-test-{Guid.NewGuid():N}.db");
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
    public async Task BackfilledWorkerResourceLimits_AreTheDefaultLimits()
    {
        // Arrange
        await InsertPreUpgradeGlobalSettingsRowAsync();

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        string json = await LoadColumnAsync(SelectLimitsSql);
        WorkerResourceLimits limits = JsonSerializer.Deserialize<WorkerResourceLimits>(json)!;
        (limits.MemoryLimitMb, limits.CpuLimit, limits.PidsLimit).ShouldBe((FrozenMemoryLimitMb, FrozenCpuLimit, FrozenPidsLimit));
    }

    /// <summary>
    /// Inserts a global_settings row via raw ADO.NET, filling only the columns that are NOT NULL
    /// without a default at the previous migration — the new worker columns do not exist yet.
    /// </summary>
    private async Task InsertPreUpgradeGlobalSettingsRowAsync()
    {
        List<(string Name, string Type)> required = [];
        using (SqliteCommand info = _connection.CreateCommand())
        {
            info.CommandText = "SELECT name, type FROM pragma_table_info('global_settings') WHERE \"notnull\" = 1 AND dflt_value IS NULL;";
            using SqliteDataReader reader = await info.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                required.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        using SqliteCommand insert = _connection.CreateCommand();
#pragma warning disable CA2100 // Column names come from the SQLite schema pragma, never user input; values are parameterized.
        insert.CommandText =
            $"INSERT INTO global_settings ({string.Join(", ", required.Select(c => c.Name))}) " +
            $"VALUES ({string.Join(", ", required.Select((_, i) => $"$p{i}"))});";
#pragma warning restore CA2100
        for (int i = 0; i < required.Count; i++)
        {
            insert.Parameters.AddWithValue($"$p{i}", PlaceholderFor(required[i].Name, required[i].Type));
        }

        await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static object PlaceholderFor(string columnName, string columnType)
    {
        if (columnName == "id")
        {
            return Guid.NewGuid().ToString();
        }

        return columnType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase)
            ? 1
            : "2026-01-01T00:00:00.0000000+00:00";
    }

    private async Task<string> LoadColumnAsync(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
#pragma warning disable CA2100 // Callers pass only the fixed SELECT constants declared in this class.
        command.CommandText = sql;
#pragma warning restore CA2100
        object? value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return (string)value!;
    }
}
