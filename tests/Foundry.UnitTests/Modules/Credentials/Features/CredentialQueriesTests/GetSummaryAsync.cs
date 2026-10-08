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
            account.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.Present("encrypted-key")));
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
            () => summary.ApiKeyStatus.ShouldBe(CredentialQueries.ApiKeyStatusPresent),
            () => summary.SubscriptionType.ShouldBeNull(),
            () => summary.OAuthAccountEmail.ShouldBeNull(),
            () => summary.OAuthAccountOrgName.ShouldBeNull());
    }

    [Fact]
    public async Task WhenApiKeyAccountWithNotConfiguredCredential_ApiKeyStatusIsNotConfigured()
    {
        // Arrange — a freshly-created account starts with NotConfigured credential.
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
        summary.ApiKeyStatus.ShouldBe(CredentialQueries.ApiKeyStatusNotConfigured);
    }

    [Fact]
    public async Task WhenApiKeyAccountWithUnreadableCredential_ApiKeyStatusIsUnreadable()
    {
        // Arrange — Unreadable is produced when EF cannot decrypt the stored api_key column
        // (key rotation or corrupt base-64). Inject an undecryptable value via raw SQL so the
        // loaded account truly has an Unreadable credential and ToSummary maps it correctly.
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount seed = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(seed);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
            await seedDb.Database.ExecuteSqlRawAsync(
                "UPDATE claude_account SET api_key = {0}",
                Convert.ToBase64String([0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE]));
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        // Act
        ClaudeAccountSummary summary = CredentialQueries.ToSummary(account.ShouldNotBeNull());

        // Assert
        summary.ApiKeyStatus.ShouldBe(CredentialQueries.ApiKeyStatusUnreadable);
    }

    [Fact]
    public async Task WhenOAuthAccount_ApiKeyStatusIsNotConfigured()
    {
        // Arrange — OAuth mode has no API key credential; the status derives as NotConfigured.
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
        summary.ApiKeyStatus.ShouldBe(CredentialQueries.ApiKeyStatusNotConfigured);
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

    [Fact]
    public async Task WhenSpendStateIsAvailable_NextProbeAtIsNull()
    {
        // Arrange — default account starts with Available spend state.
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
        summary.NextProbeAt.ShouldBeNull();
    }

    [Fact]
    public async Task WhenSpendStateIsBlocked_NextProbeAtIsPopulated()
    {
        // Arrange
        DateTimeOffset nextProbeAt = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.BlockSpend(nextProbeAt);
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialQueries sut = new(dbContext);

        // Act
        ClaudeAccountSummary? result = await sut.GetSummaryAsync(TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccountSummary summary = result.ShouldNotBeNull();
        summary.NextProbeAt.ShouldBe(nextProbeAt);
    }
}
