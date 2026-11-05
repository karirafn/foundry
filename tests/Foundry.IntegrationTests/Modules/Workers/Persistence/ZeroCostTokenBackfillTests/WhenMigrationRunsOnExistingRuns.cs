using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Persistence.ZeroCostTokenBackfillTests;

/// <summary>
/// Verifies the real BackfillZeroCostEmptyModelUsageTokens migration Up() SQL by running the
/// actual EF migration against rows seeded through raw ADO.NET. Costs are seeded as TEXT (the
/// column's affinity, e.g. <c>'0.0'</c>) so a plain <c>total_cost_usd = 0</c> predicate fails.
/// </summary>
public sealed class WhenMigrationRunsOnExistingRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261105094333_AddRebaseConflictAttempts";
    private const string TargetMigrationId = "20261105130447_BackfillZeroCostEmptyModelUsageTokens";

    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenMigrationRunsOnExistingRuns()
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
    public async Task ZeroCostRunWithAllTokensNull_IsBackfilledToZero()
    {
        // Arrange — an auth-failure run: summary present, every bucket unknown, cost stored as TEXT '0.0'
        Guid id = await InsertFailedRunAsync(
            numTurns: 1, cost: "0.0", input: null, cacheRead: null, cacheCreation: null, output: null);

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await LoadTokensAsync(id)).ShouldBe((0L, 0L, 0L, 0L));
    }

    [Fact]
    public async Task LegacyRunWithKnownInputAndOutput_IsUnchanged()
    {
        // Arrange — predates cache-column persistence: cache buckets null, input/output known
        Guid id = await InsertFailedRunAsync(
            numTurns: 3, cost: "0.0", input: 120, cacheRead: null, cacheCreation: null, output: 45);

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await LoadTokensAsync(id)).ShouldBe((120L, null, null, 45L));
    }

    [Fact]
    public async Task NonZeroCostRunWithAllTokensNull_IsUnchanged()
    {
        // Arrange
        Guid id = await InsertFailedRunAsync(
            numTurns: 2, cost: "0.42", input: null, cacheRead: null, cacheCreation: null, output: null);

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await LoadTokensAsync(id)).ShouldBe((null, null, null, null));
    }

    [Fact]
    public async Task RunWithoutResultSummary_IsUnchanged()
    {
        // Arrange — num_turns null means no result summary was recorded
        Guid id = await InsertFailedRunAsync(
            numTurns: null, cost: null, input: null, cacheRead: null, cacheCreation: null, output: null);

        // Act
        await _dbContext.Database.MigrateAsync(TargetMigrationId, TestContext.Current.CancellationToken);

        // Assert
        (await LoadTokensAsync(id)).ShouldBe((null, null, null, null));
    }

    private async Task<Guid> InsertFailedRunAsync(
        int? numTurns,
        string? cost,
        long? input,
        long? cacheRead,
        long? cacheCreation,
        long? output)
    {
        Guid id = Guid.NewGuid();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO worker_runs (id, created_at, issue_id, state, failed_at, reason, num_turns, total_cost_usd, " +
            "input_tokens, cache_read_input_tokens, cache_creation_input_tokens, output_tokens) " +
            "VALUES ($id, '2026-11-01T00:00:00.0000000+00:00', $issueId, 'failed', '2026-11-01T00:05:00.0000000+00:00', " +
            "'auth_invalid', $numTurns, $cost, $input, $cacheRead, $cacheCreation, $output);";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$issueId", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$numTurns", DbValue(numTurns));
        command.Parameters.Add("$cost", SqliteType.Text).Value = (object?)cost ?? DBNull.Value;
        command.Parameters.AddWithValue("$input", DbValue(input));
        command.Parameters.AddWithValue("$cacheRead", DbValue(cacheRead));
        command.Parameters.AddWithValue("$cacheCreation", DbValue(cacheCreation));
        command.Parameters.AddWithValue("$output", DbValue(output));
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private async Task<(long?, long?, long?, long?)> LoadTokensAsync(Guid id)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "SELECT input_tokens, cache_read_input_tokens, cache_creation_input_tokens, output_tokens " +
            "FROM worker_runs WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        using SqliteDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
        return (Read(reader, 0), Read(reader, 1), Read(reader, 2), Read(reader, 3));
    }

    private static object DbValue<T>(T? value)
        where T : struct =>
        value.HasValue ? value.Value : DBNull.Value;

    private static long? Read(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
}
