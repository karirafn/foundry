using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Foundry.WebApi.Persistence;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.DispatchPauses;

/// <summary>
/// Runs the MoveDispatchPauseToWorkers migration over a settings row carrying live pause state, so the state a running
/// Foundry holds survives the move to the dispatch_pause table and back.
/// </summary>
public sealed class WhenMoveDispatchPauseToWorkersMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261119162005_ReplacePullRequestBodyWithDescription";
    private const string TargetMigrationId = "20261119181808_MoveDispatchPauseToWorkers";
    private const string SettingsId = "00000000-0000-0000-0000-000000000001";
    private const string UsageLimitResetsAt = "2026-11-20 08:00:00+00:00";
    private const string HostPauses =
        "[{\"Host\":\"gitlab.com\",\"PausedAt\":\"2026-11-19T07:39:00+00:00\",\"ProbeAttempts\":2,\"NextProbeAt\":\"2026-11-19T07:52:00+00:00\"}]";
    private const string PackageProxyPause =
        "{\"PausedAt\":\"2026-11-19T07:40:00+00:00\",\"ProbeAttempts\":1,\"NextProbeAt\":\"2026-11-19T07:43:00+00:00\"}";
    private const string SelectFromDispatchPause =
        "SELECT id, is_dispatch_paused, usage_limit_resets_at, host_pauses, package_proxy_pause FROM dispatch_pause;";
    private const string SelectFromGlobalSettings =
        "SELECT id, is_dispatch_paused, usage_limit_resets_at, host_pauses, package_proxy_pause FROM global_settings;";

    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenMoveDispatchPauseToWorkersMigrationRuns()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dispatch-pause-migration-{Guid.NewGuid():N}.db");
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
    public async Task PausedSettingsRow_IsCopiedToTheDispatchPauseRow()
    {
        // Arrange
        await InsertPausedSettingsAsync();

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        PauseColumns moved = await ReadSingleRowAsync(fromDispatchPause: true);
        moved.ShouldBe(new PauseColumns(SettingsId, 1, UsageLimitResetsAt, HostPauses, PackageProxyPause));
    }

    [Fact]
    public async Task NoSettingsRow_InsertsNoDispatchPauseRow()
    {
        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dispatch_pause;";
        long count = (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        count.ShouldBe(0);
    }

    [Fact]
    public async Task MovedColumns_AreDroppedFromGlobalSettings()
    {
        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM pragma_table_info('global_settings') "
            + "WHERE name IN ('is_dispatch_paused', 'usage_limit_resets_at', 'host_pauses', 'package_proxy_pause');";
        long count = (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        count.ShouldBe(0);
    }

    [Fact]
    public async Task Down_CopiesThePauseStateBackToGlobalSettings()
    {
        // Arrange
        await InsertPausedSettingsAsync();
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Act
        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);

        // Assert
        PauseColumns restored = await ReadSingleRowAsync(fromDispatchPause: false);
        restored.ShouldBe(new PauseColumns(SettingsId, 1, UsageLimitResetsAt, HostPauses, PackageProxyPause));
    }

    private async Task InsertPausedSettingsAsync()
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO global_settings "
            + "(id, max_concurrent, timeout_minutes, probe_interval_minutes, auto_resume_on_usage_reset, nested_docker_enabled, "
            + " allowed_provider_hosts, claude_code_settings, image_build_status, worker_image_configuration, "
            + " worker_resource_limits, created_at, updated_at, "
            + " is_dispatch_paused, usage_limit_resets_at, host_pauses, package_proxy_pause) "
            + "VALUES ($id, 1, 120, 60, 1, 0, '[]', '{}', '{}', '{}', '{}', datetime('now'), datetime('now'), "
            + " 1, $resetsAt, $hostPauses, $packageProxyPause);";
        command.Parameters.AddWithValue("$id", SettingsId);
        command.Parameters.AddWithValue("$resetsAt", UsageLimitResetsAt);
        command.Parameters.AddWithValue("$hostPauses", HostPauses);
        command.Parameters.AddWithValue("$packageProxyPause", PackageProxyPause);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<PauseColumns> ReadSingleRowAsync(bool fromDispatchPause)
    {
        using SqliteCommand command = _connection.CreateCommand();
        if (fromDispatchPause)
        {
            command.CommandText = SelectFromDispatchPause;
        }
        else
        {
            command.CommandText = SelectFromGlobalSettings;
        }

        using SqliteDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
        PauseColumns row = new(
            reader.GetString(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4));
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        return row;
    }

    private sealed record PauseColumns(
        string Id,
        long IsDispatchPaused,
        string UsageLimitResetsAt,
        string HostPauses,
        string PackageProxyPause);
}
