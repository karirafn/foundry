using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.UpdateAuthModeTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public HandleAsync()
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
    public async Task WhenSwitchingToApiKeyMode_UpdatesAuthModeAndReturnsSummary()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = new(dbContext);

        UpdateAuthMode.Command command = new("api_key", "sk-ant-abc123");

        // Act
        Result<ClaudeAccountSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccountSummary summary = result.ShouldBeOfType<Result<ClaudeAccountSummary>.Success>().Value;
        summary.AuthMode.ShouldBe("ApiKey");
    }

    [Fact]
    public async Task WhenSwitchingToOAuthMode_SetsConfiguredMarkerWithNoScan()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.SetAuthMode(new AuthMode.ApiKey("old-key"));
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = new(dbContext);

        UpdateAuthMode.Command command = new("oauth", null);

        // Act
        Result<ClaudeAccountSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccountSummary summary = result.ShouldBeOfType<Result<ClaudeAccountSummary>.Success>().Value;
        summary.AuthMode.ShouldBe("OAuth");
    }

    [Fact]
    public async Task WhenSwitchingToOAuthMode_PersistsOAuthMarkerWithoutSubscriptionType()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateAuthMode.Handler sut = new(dbContext);
            UpdateAuthMode.Command command = new("oauth", null);
            await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        ClaudeAccount persisted = (await assertDb.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken))!;
        AuthMode.OAuth oauth = persisted.AuthMode.ShouldBeOfType<AuthMode.OAuth>();
        oauth.SubscriptionType.ShouldBeNull();
    }

    [Fact]
    public async Task WhenAccountNotFound_ReturnsNotFoundError()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = new(dbContext);

        UpdateAuthMode.Command command = new("api_key", "sk-ant-abc123");

        // Act
        Result<ClaudeAccountSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<ClaudeAccountSummary>.Failure failure =
            result.ShouldBeOfType<Result<ClaudeAccountSummary>.Failure>();
        failure.Error.Code.ShouldBe(CredentialsErrors.NotFoundCode);
    }

    [Fact]
    public async Task WhenSwitchingToApiKeyMode_PersistsChangesToDatabase()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount seed = ClaudeAccount.Create();
            seed.SetAuthMode(new AuthMode.OAuth(SubscriptionType: null));
            seedDb.Set<ClaudeAccount>().Add(seed);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateAuthMode.Handler sut = new(dbContext);
            UpdateAuthMode.Command command = new("api_key", "sk-ant-new-key");
            await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        ClaudeAccount account = (await assertDb.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken))!;
        account.AuthMode.ShouldBeOfType<AuthMode.ApiKey>();
    }

    [Fact]
    public async Task WhenSwitchingToApiKeyMode_ClearsOAuthIdentityAndSetsValidValidity()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount seed = ClaudeAccount.Create();
            seed.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");
            seedDb.Set<ClaudeAccount>().Add(seed);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateAuthMode.Handler sut = new(dbContext);
            UpdateAuthMode.Command command = new("api_key", "sk-ant-new-key");
            await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        ClaudeAccount account = (await assertDb.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken))!;
        account.ShouldSatisfyAllConditions(
            () => account.OAuthAccountEmail.ShouldBeNull(),
            () => account.OAuthAccountOrgName.ShouldBeNull(),
            () => account.Validity.ShouldBeOfType<CredentialValidity.Valid>());
    }
}
