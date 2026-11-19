using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.ClaudeAccountDbContextExtensionsTests;

public sealed class FindClaudeAccountNoTrackingAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public FindClaudeAccountNoTrackingAsync()
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
    public async Task WhenRowExists_ReturnsItUntrackedByItsKey()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            seedDb.Set<ClaudeAccount>().Add(ClaudeAccount.Create());
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();

        // Act
        ClaudeAccount? result = await dbContext.FindClaudeAccountNoTrackingAsync(TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccount account = result.ShouldNotBeNull();
        account.ShouldSatisfyAllConditions(
            () => account.Id.ShouldBe(ClaudeAccountId.Default),
            () => dbContext.Entry(account).State.ShouldBe(EntityState.Detached));
    }

    [Fact]
    public async Task WhenNoRowExists_ReturnsNull()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();

        // Act
        ClaudeAccount? result = await dbContext.FindClaudeAccountNoTrackingAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }
}
