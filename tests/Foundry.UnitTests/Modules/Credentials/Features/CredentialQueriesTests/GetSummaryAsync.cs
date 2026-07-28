using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.CredentialQueriesTests;

public sealed class GetSummaryAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetSummaryAsync()
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
    public async Task WhenNoAccountExists_ReturnsNull()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialQueries sut = new(dbContext);

        // Act
        ClaudeAccountSummary? result = await sut.GetSummaryAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WhenApiKeyAccount_ReturnsApiKeyModeWithNotConfiguredOAuthStatus()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.SetAuthMode(new AuthMode.ApiKey("encrypted-key"));
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialQueries sut = new(dbContext);

        // Act
        ClaudeAccountSummary? result = await sut.GetSummaryAsync(TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccountSummary summary = result.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.AuthMode.ShouldBe("ApiKey"),
            () => summary.OAuthStatus.ShouldBe(CredentialQueries.OAuthStatusNotConfigured),
            () => summary.SubscriptionType.ShouldBeNull(),
            () => summary.OAuthAccountEmail.ShouldBeNull(),
            () => summary.OAuthAccountOrgName.ShouldBeNull());
    }

    [Fact]
    public async Task WhenOAuthAccountWithoutEmail_ReturnsOAuthModeWithReLoginNeededStatus()
    {
        // Arrange — OAuth mode without a committed account email returns ReLoginNeeded.
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.SetAuthMode(new AuthMode.OAuth("pro"));
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialQueries sut = new(dbContext);

        // Act
        ClaudeAccountSummary? result = await sut.GetSummaryAsync(TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccountSummary summary = result.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.AuthMode.ShouldBe("OAuth"),
            () => summary.OAuthStatus.ShouldBe(CredentialQueries.OAuthStatusReLoginNeeded),
            () => summary.SubscriptionType.ShouldBe("pro"));
    }

    [Fact]
    public async Task WhenOAuthAccountWithEmail_ReturnsOAuthModeWithPresentStatus()
    {
        // Arrange — OAuth mode with committed email returns Present.
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialQueries sut = new(dbContext);

        // Act
        ClaudeAccountSummary? result = await sut.GetSummaryAsync(TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccountSummary summary = result.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.AuthMode.ShouldBe("OAuth"),
            () => summary.OAuthStatus.ShouldBe(CredentialQueries.OAuthStatusPresent),
            () => summary.SubscriptionType.ShouldBe("pro"),
            () => summary.OAuthAccountEmail.ShouldBe("user@example.com"),
            () => summary.OAuthAccountOrgName.ShouldBe("MyOrg"));
    }

    [Fact]
    public async Task WhenOAuthAccountIsInvalid_ReturnsReLoginNeededStatus()
    {
        // Arrange — Invalid validity is the persisted signal that re-login is needed.
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");
            account.Invalidate("worker_auth_failed");
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialQueries sut = new(dbContext);

        // Act
        ClaudeAccountSummary? result = await sut.GetSummaryAsync(TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccountSummary summary = result.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.AuthMode.ShouldBe("OAuth"),
            () => summary.OAuthStatus.ShouldBe(CredentialQueries.OAuthStatusReLoginNeeded));
    }

    [Fact]
    public async Task WhenAccountExists_ReturnsAccountId()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialQueries sut = new(dbContext);

        // Act
        ClaudeAccountSummary? result = await sut.GetSummaryAsync(TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccountSummary summary = result.ShouldNotBeNull();
        summary.AccountId.ShouldBe(ClaudeAccountId.Default.Value);
    }
}
