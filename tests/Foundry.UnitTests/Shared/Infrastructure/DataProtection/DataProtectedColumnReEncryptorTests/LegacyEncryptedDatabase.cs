using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.DataProtection;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Foundry.UnitTests.Shared.Infrastructure.DataProtection.DataProtectedColumnReEncryptorTests;

/// <summary>
/// An in-memory SQLite database whose encrypted columns were written under the "legacy"
/// discriminator, sharing one key ring with the "Foundry" (current) discriminator.
/// </summary>
internal sealed class LegacyEncryptedDatabase : IAsyncDisposable
{
    internal const string TokenValue = "ghp_worker";
    internal const string BranchRulesTokenValue = "ghp_rules";
    internal const string ApiKeyValue = "sk-ant-test";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly EphemeralDataProtectionProvider _root = new();

    internal LegacyEncryptedDatabase()
    {
        Current = _root.CreateProtector("Foundry");
        Legacy = _root.CreateProtector("legacy");
        Unrelated = _root.CreateProtector("unrelated");
        _connection.Open();

        using FoundryDbContext dbContext = CreateDbContext(Legacy);
        dbContext.Database.EnsureCreated();
    }

    internal IDataProtectionProvider Current { get; }

    internal IDataProtectionProvider Legacy { get; }

    internal IDataProtectionProvider Unrelated { get; }

    internal string WebhookSecret { get; private set; } = string.Empty;

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    internal FoundryDbContext CreateDbContext(IDataProtectionProvider provider, params IInterceptor[] interceptors)
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .Options;

        return new FoundryDbContext(options, provider);
    }

    /// <summary>Seeds one value into every encrypted column, protected by <paramref name="provider"/>.</summary>
    internal async Task SeedAsync(IDataProtectionProvider provider, CancellationToken cancellationToken)
    {
        MonitoredRepository repository = new MonitoredRepositoryBuilder().Build();
        WebhookSecret = repository.EnsureWebhookSecret();
        ClaudeAccount account = new ClaudeAccountBuilder().WithApiKey(ApiKeyValue).Build();
        GitHubCredential credential = GitHubCredential.Create(
            "my-org",
            TokenValue,
            BaseUrl.Create("https://github.com").ValueOrThrow(),
            BranchRulesTokenValue);

        await using FoundryDbContext dbContext = CreateDbContext(provider);
        dbContext.AddRange(repository, account, credential);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Reads the raw stored text of every encrypted column, keyed by table, column and row id.</summary>
    internal async Task<Dictionary<string, string?>> ReadRawAsync(CancellationToken cancellationToken)
    {
        await using FoundryDbContext dbContext = CreateDbContext(Current);
        ISqlGenerationHelper sql = dbContext.GetService<ISqlGenerationHelper>();
        Dictionary<string, string?> raw = [];

        foreach (EncryptedColumn column in EncryptedColumn.DiscoverIn(dbContext.Model))
        {
            string select =
                $"SELECT {sql.DelimitIdentifier(column.KeyColumn)} AS RowId, {sql.DelimitIdentifier(column.Column)} AS StoredValue " +
                $"FROM {sql.DelimitIdentifier(column.Table)}";
            List<RawRow> rows = await dbContext.Database
                .SqlQueryRaw<RawRow>(select)
                .ToListAsync(cancellationToken);

            foreach (RawRow row in rows)
            {
                raw[$"{column.Table}.{column.Column}.{row.RowId}"] = row.StoredValue;
            }
        }

        return raw;
    }

    internal async Task SetApiKeyColumnAsync(string? storedValue, CancellationToken cancellationToken)
    {
        await using FoundryDbContext dbContext = CreateDbContext(Current);
        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE claude_account SET api_key = @p0",
            [new SqliteParameter("p0", (object?)storedValue ?? DBNull.Value)],
            cancellationToken);
    }

    private sealed class RawRow
    {
        public string RowId { get; init; } = string.Empty;

        public string? StoredValue { get; init; }
    }
}
