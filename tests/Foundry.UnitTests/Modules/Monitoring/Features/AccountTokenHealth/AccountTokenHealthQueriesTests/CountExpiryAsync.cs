using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.AccountTokenHealth;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.AccountTokenHealth.AccountTokenHealthQueriesTests;

/// <summary>
/// <see cref="AccountTokenHealthQueries.CountExpiryAsync"/> counts the Valid accounts whose token expiry is
/// expiring soon or expired at the given instant, and nothing else.
/// </summary>
public sealed class CountExpiryAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public CountExpiryAsync()
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

    [Fact]
    public async Task WhenNoAccountExists_ReturnsZeroForBoth()
    {
        // Act
        AccountTokenExpiryCounts counts = await CountAsync();

        // Assert
        counts.ShouldBe(new AccountTokenExpiryCounts(ExpiringSoon: 0, Expired: 0));
    }

    [Fact]
    public async Task WhenValidAccountsAreExpiringSoonOrExpired_CountsEach()
    {
        // Arrange
        await SeedAsync("soon-a", new TokenExpiry.On(Now.AddDays(3)));
        await SeedAsync("soon-edge", new TokenExpiry.On(Now + TokenExpiry.WarningWindow));
        await SeedAsync("expired", new TokenExpiry.On(Now.AddDays(-1)));

        // Act
        AccountTokenExpiryCounts counts = await CountAsync();

        // Assert
        counts.ShouldBe(new AccountTokenExpiryCounts(ExpiringSoon: 2, Expired: 1));
    }

    [Fact]
    public async Task WhenAnExpiringOrExpiredAccountIsInvalid_ItIsNotCounted()
    {
        // Arrange
        await SeedAsync("invalid-soon", new TokenExpiry.On(Now.AddDays(3)), invalidSince: Now.AddHours(-1));
        await SeedAsync("invalid-expired", new TokenExpiry.On(Now.AddDays(-3)), invalidSince: Now.AddHours(-1));

        // Act
        AccountTokenExpiryCounts counts = await CountAsync();

        // Assert
        counts.ShouldBe(new AccountTokenExpiryCounts(ExpiringSoon: 0, Expired: 0));
    }

    [Fact]
    public async Task WhenExpiryIsNotCheckedUnavailableNeverOrCurrent_ItIsNotCounted()
    {
        // Arrange
        await SeedAsync("not-checked", new TokenExpiry.NotChecked());
        await SeedAsync("unavailable", new TokenExpiry.Unavailable());
        await SeedAsync("never", new TokenExpiry.Never());
        await SeedAsync("current", new TokenExpiry.On(Now + TokenExpiry.WarningWindow + TimeSpan.FromSeconds(1)));

        // Act
        AccountTokenExpiryCounts counts = await CountAsync();

        // Assert
        counts.ShouldBe(new AccountTokenExpiryCounts(ExpiringSoon: 0, Expired: 0));
    }

    [Fact]
    public async Task WhenAccountHasNoToken_ItIsNotCounted()
    {
        // Arrange
        await using (FoundryDbContext db = CreateDbContext())
        {
            db.Set<Credential>().Add(GitHubCredential.Create(
                AccountName.Create("tokenless").ValueOrThrow(),
                null,
                BaseUrl.Create("https://github.com").ValueOrThrow()));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act
        AccountTokenExpiryCounts counts = await CountAsync();

        // Assert
        counts.ShouldBe(new AccountTokenExpiryCounts(ExpiringSoon: 0, Expired: 0));
    }

    private FoundryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);

    private async Task<AccountTokenExpiryCounts> CountAsync()
    {
        await using FoundryDbContext db = CreateDbContext();
        return await new AccountTokenHealthQueries(db).CountExpiryAsync(Now, TestContext.Current.CancellationToken);
    }

    private async Task SeedAsync(string name, TokenExpiry expiry, DateTimeOffset? invalidSince = null)
    {
        await using FoundryDbContext db = CreateDbContext();
        GitHubCredential account = GitHubCredential.Create(
            AccountName.Create(name).ValueOrThrow(),
            new IssuedToken("token-" + name, expiry),
            BaseUrl.Create("https://github.com").ValueOrThrow());

        if (invalidSince is { } since)
        {
            account.MarkTokenInvalid(since);
        }

        db.Set<Credential>().Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
