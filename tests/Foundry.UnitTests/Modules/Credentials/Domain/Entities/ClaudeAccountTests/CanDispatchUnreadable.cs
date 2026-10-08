using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Domain.Entities.ClaudeAccountTests;

/// <summary>
/// DB-backed coverage for the <see cref="ClaudeAccount.CanDispatch"/> Unreadable branch.
/// Unreadable is only produced via EF when the converter cannot decrypt the stored api_key
/// column, so this scenario requires a real SQLite database to exercise the EF converter path.
/// Pure in-memory CanDispatch tests live in <see cref="CanDispatch"/>.
/// </summary>
public sealed class CanDispatchUnreadable : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public CanDispatchUnreadable()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    [Fact]
    public async Task WhenApiKeyUnreadable_ReturnsFalse()
    {
        // Arrange — Unreadable is only produced via EF when the converter cannot decrypt the stored
        // api_key column. Inject an undecryptable value via raw SQL so the loaded account truly has
        // an Unreadable credential (SetAuthMode rejects Unreadable as a write-time guard).
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
            await seedDb.Database.ExecuteSqlRawAsync(
                "UPDATE claude_account SET api_key = {0}",
                Convert.ToBase64String([0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE]));
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        ClaudeAccount? loaded = await dbContext.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        // Act
        bool result = loaded.ShouldNotBeNull().CanDispatch;

        // Assert
        result.ShouldBeFalse();
    }
}
