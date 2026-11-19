using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.ClaudeAccountDbContextExtensionsTests;

public sealed class FindClaudeAccountAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public FindClaudeAccountAsync()
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

    private async Task SeedAsync()
    {
        await using FoundryDbContext seedDb = CreateDbContext();
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "My Org", "pro");
        seedDb.Set<ClaudeAccount>().Add(account);
        await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenRowExists_ReturnsItTrackedByItsKey()
    {
        // Arrange
        await SeedAsync();
        await using FoundryDbContext dbContext = CreateDbContext();

        // Act
        ClaudeAccount? result = await dbContext.FindClaudeAccountAsync(TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccount account = result.ShouldNotBeNull();
        account.ShouldSatisfyAllConditions(
            () => account.Id.ShouldBe(ClaudeAccountId.Default),
            () => account.OAuthAccountEmail.ShouldBe("user@example.com"),
            () => dbContext.Entry(account).State.ShouldBe(EntityState.Unchanged));
    }

    [Fact]
    public async Task WhenNoRowExists_ReturnsNull()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();

        // Act
        ClaudeAccount? result = await dbContext.FindClaudeAccountAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WhenOnlyRowHasAnotherKey_ReturnsNull()
    {
        // Arrange
        await SeedAsync();
        await using FoundryDbContext dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE claude_account SET id = '11111111-1111-1111-1111-111111111111'",
            TestContext.Current.CancellationToken);

        // Act
        ClaudeAccount? result = await dbContext.FindClaudeAccountAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }
}
