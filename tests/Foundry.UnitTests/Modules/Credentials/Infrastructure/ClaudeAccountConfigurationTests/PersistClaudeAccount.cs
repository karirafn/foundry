using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Infrastructure.ClaudeAccountConfigurationTests;

public sealed class PersistClaudeAccount : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistClaudeAccount()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        IDataProtectionProvider provider = DataProtectionProvider.Create("TestApp");
        _dbContext = new FoundryDbContext(options, provider);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenApiKeyAccountPersisted_CanBeReloadedWithAllProperties()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();

        _dbContext.Set<ClaudeAccount>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        ClaudeAccount? result = await _dbContext
            .Set<ClaudeAccount>()
            .FindAsync([account.Id], TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccount reloaded = result.ShouldNotBeNull();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.Id.ShouldBe(account.Id),
            () => reloaded.AuthMode.ShouldBeOfType<AuthMode.ApiKey>(),
            () => reloaded.Validity.ShouldBeOfType<CredentialValidity.Valid>(),
            () => reloaded.OAuthAccountEmail.ShouldBeNull(),
            () => reloaded.OAuthAccountOrgName.ShouldBeNull(),
            () => reloaded.CreatedAt.ShouldBe(account.CreatedAt),
            () => reloaded.UpdatedAt.ShouldBe(account.UpdatedAt));
    }

    [Fact]
    public async Task WhenOAuthAccountPersisted_CanBeReloadedWithSubscriptionType()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");

        _dbContext.Set<ClaudeAccount>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        ClaudeAccount? result = await _dbContext
            .Set<ClaudeAccount>()
            .FindAsync([account.Id], TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccount reloaded = result.ShouldNotBeNull();
        AuthMode.OAuth reloadedOauth = reloaded.AuthMode.ShouldBeOfType<AuthMode.OAuth>();
        reloadedOauth.SubscriptionType.ShouldBe("pro");
    }

    [Fact]
    public async Task WhenInvalidValidityPersisted_CanBeReloadedWithReason()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.Invalidate("worker_auth_failed");

        _dbContext.Set<ClaudeAccount>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        ClaudeAccount? result = await _dbContext
            .Set<ClaudeAccount>()
            .FindAsync([account.Id], TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccount reloaded = result.ShouldNotBeNull();
        CredentialValidity.Invalid reloadedInvalid = reloaded.Validity.ShouldBeOfType<CredentialValidity.Invalid>();
        reloadedInvalid.Reason.ShouldBe("worker_auth_failed");
    }

    [Fact]
    public async Task WhenOAuthIdentityPersisted_CanBeReloadedWithAllIdentityFields()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");

        _dbContext.Set<ClaudeAccount>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        ClaudeAccount? result = await _dbContext
            .Set<ClaudeAccount>()
            .FindAsync([account.Id], TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccount reloaded = result.ShouldNotBeNull();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.OAuthAccountEmail.ShouldBe("user@example.com"),
            () => reloaded.OAuthAccountOrgName.ShouldBe("MyOrg"));
    }

    [Fact]
    public async Task WhenApiKeyAuthModePersisted_AuthModeColumnIsEncrypted()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.SetAuthMode(new AuthMode.ApiKey("my-plaintext-key"));

        _dbContext.Set<ClaudeAccount>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act — query raw column value to verify encryption
        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT auth_mode FROM claude_account LIMIT 1";
        string? rawValue = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert — the raw stored value should not contain plaintext JSON
        string nonNull = rawValue.ShouldNotBeNull();
        nonNull.ShouldNotContain("api_key");
        nonNull.ShouldNotContain("my-plaintext-key");
    }

    [Fact]
    public void OAuthAccountEmail_HasMaxLength_MatchingDomainConstant()
    {
        // Arrange
        IEntityType entityType = _dbContext.Model.FindEntityType(typeof(ClaudeAccount))!;

        // Act
        IProperty property = entityType.FindProperty(nameof(ClaudeAccount.OAuthAccountEmail))!;

        // Assert
        property.GetMaxLength().ShouldBe(ClaudeAccount.MaxOAuthAccountEmailLength);
    }

    [Fact]
    public void OAuthAccountOrgName_HasMaxLength_MatchingDomainConstant()
    {
        // Arrange
        IEntityType entityType = _dbContext.Model.FindEntityType(typeof(ClaudeAccount))!;

        // Act
        IProperty property = entityType.FindProperty(nameof(ClaudeAccount.OAuthAccountOrgName))!;

        // Assert
        property.GetMaxLength().ShouldBe(ClaudeAccount.MaxOAuthAccountOrgNameLength);
    }

    [Fact]
    public async Task WhenBlockedSpendStatePersisted_CanBeReloadedAsBlocked()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.BlockSpend(DateTimeOffset.UtcNow.AddHours(1));

        _dbContext.Set<ClaudeAccount>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        ClaudeAccount? result = await _dbContext
            .Set<ClaudeAccount>()
            .FindAsync([account.Id], TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccount reloaded = result.ShouldNotBeNull();
        reloaded.SpendState.ShouldBeOfType<SpendState.Blocked>();
    }

    [Fact]
    public async Task WhenAvailableSpendStatePersisted_CanBeReloadedAsAvailable()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();

        _dbContext.Set<ClaudeAccount>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        ClaudeAccount? result = await _dbContext
            .Set<ClaudeAccount>()
            .FindAsync([account.Id], TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccount reloaded = result.ShouldNotBeNull();
        reloaded.SpendState.ShouldBeOfType<SpendState.Available>();
    }
}
