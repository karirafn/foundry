using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Domain.Entities.ClaudeAccountTests;

public sealed class CanDispatch : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public CanDispatch()
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
    public void WhenOAuthModeAndValidAndAvailable_ReturnsTrue()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");

        // Act
        bool result = account.CanDispatch;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenApiKeyPresentAndValidAndAvailable_ReturnsTrue()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.Present("sk-ant-abc")));

        // Act
        bool result = account.CanDispatch;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenApiKeyNotConfigured_ReturnsFalse()
    {
        // Arrange — Create() seeds NotConfigured by default.
        ClaudeAccount account = ClaudeAccount.Create();

        // Act
        bool result = account.CanDispatch;

        // Assert
        result.ShouldBeFalse();
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

    [Fact]
    public void WhenValidityIsInvalid_ReturnsFalse()
    {
        // Arrange — OAuth so key check passes; validity check fires.
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");
        account.Invalidate("worker_auth_failed");

        // Act
        bool result = account.CanDispatch;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenSpendIsBlocked_ReturnsFalse()
    {
        // Arrange — OAuth + valid; spend check fires.
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");
        account.BlockSpend(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        bool result = account.CanDispatch;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenValidityInvalidAndSpendBlocked_ReturnsFalse()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");
        account.Invalidate("worker_auth_failed");
        account.BlockSpend(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        bool result = account.CanDispatch;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenApiKeyPresentButInvalid_ReturnsFalse()
    {
        // Arrange — key is Present so key check passes; validity check fires.
        ClaudeAccount account = ClaudeAccount.Create();
        account.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.Present("sk-ant-abc")));
        account.Invalidate("worker_auth_failed");

        // Act
        bool result = account.CanDispatch;

        // Assert
        result.ShouldBeFalse();
    }
}
