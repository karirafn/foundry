using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Persistence.WorkerRoleModelMigrationTests;

/// <summary>
/// Runs the real AddWorkerRoleModelAndRemoveGlobalModel migration against a settings row seeded through raw ADO.NET:
/// the global model moves to the implement role and is removed from the claude_code_settings JSON (ADR 0095).
/// </summary>
public sealed class WhenMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261106113519_AddWorkerRolesAndRemoveWorkerPrompts";
    private const string TargetMigrationId = "20261106130722_AddWorkerRoleModelAndRemoveGlobalModel";

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

    [Fact]
    public async Task APinnedGlobalModel_BecomesTheImplementRolesModel()
    {
        // Arrange
        await InsertSettingsAsync("""{"Model":"  claude-opus-4-1  ","BlockCiCdEdits":false,"AdditionalDenyRules":["Bash(rm:*)"]}""");

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await ScalarAsync("SELECT model FROM worker_role_settings WHERE role = 'implement';")).ShouldBe("claude-opus-4-1");
    }

    [Fact]
    public async Task TheGlobalModel_IsRemovedFromTheSettingsJson()
    {
        // Arrange
        await InsertSettingsAsync("""{"Model":"opus","BlockCiCdEdits":false,"AdditionalDenyRules":["Bash(rm:*)"]}""");

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await ScalarAsync("SELECT claude_code_settings FROM global_settings;"))
            .ShouldBe("""{"BlockCiCdEdits":false,"AdditionalDenyRules":["Bash(rm:*)"]}""");
    }

    [Fact]
    public async Task AnEmptyGlobalModel_LeavesTheRoleUnset()
    {
        // Arrange
        await InsertSettingsAsync("""{"Model":"","BlockCiCdEdits":true,"AdditionalDenyRules":[]}""");

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await ScalarAsync("SELECT count(*) FROM worker_role_settings;")).ShouldBe("0");
        (await ScalarAsync("SELECT claude_code_settings FROM global_settings;"))
            .ShouldBe("""{"BlockCiCdEdits":true,"AdditionalDenyRules":[]}""");
    }

    // Fills every NOT NULL column without a default generically so the test does not pin the settings schema.
    private async Task InsertSettingsAsync(string claudeCodeSettingsJson)
    {
        List<(string Name, string Type)> columns = [];

        using (SqliteCommand info = _connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(global_settings);";
            using SqliteDataReader reader = await info.ExecuteReaderAsync(TestContext.Current.CancellationToken);

            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                bool notNull = reader.GetInt64(3) == 1;
                bool hasDefault = !await reader.IsDBNullAsync(4, TestContext.Current.CancellationToken);
                bool primaryKey = reader.GetInt64(5) > 0;
                string name = reader.GetString(1);

                if (name == "claude_code_settings" || primaryKey || (notNull && !hasDefault))
                {
                    columns.Add((name, reader.GetString(2)));
                }
            }
        }

#pragma warning disable CA2100 // Column names come from PRAGMA table_info; values are parameters.
        using SqliteCommand insert = _connection.CreateCommand();
        insert.CommandText =
            $"INSERT INTO global_settings ({string.Join(", ", columns.Select(c => c.Name))}) " +
            $"VALUES ({string.Join(", ", columns.Select((_, i) => $"$p{i}"))});";

        for (int i = 0; i < columns.Count; i++)
        {
            (string name, string type) = columns[i];
            object value = name switch
            {
                "claude_code_settings" => claudeCodeSettingsJson,
                "id" => Guid.NewGuid().ToString(),
                _ when type.Contains("INT", StringComparison.OrdinalIgnoreCase) => 0,
                _ when type.Contains("REAL", StringComparison.OrdinalIgnoreCase) => 0.0,
                _ when name.EndsWith("_at", StringComparison.Ordinal) => "2026-11-06T00:00:00.0000000+00:00",
                _ => "{}",
            };
            insert.Parameters.AddWithValue($"$p{i}", value);
        }

        await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
#pragma warning restore CA2100
    }

    private async Task<string?> ScalarAsync(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
#pragma warning disable CA2100 // Test-owned constant SQL.
        command.CommandText = sql;
#pragma warning restore CA2100
        object? value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is null or DBNull ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
