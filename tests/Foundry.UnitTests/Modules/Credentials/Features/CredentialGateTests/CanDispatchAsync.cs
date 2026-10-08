using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.CredentialGateTests;

public sealed class CanDispatchAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public CanDispatchAsync()
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
    public async Task WhenOAuthAndLoginNotActive_ReturnsTrue()
    {
        // Arrange — OAuth mode is the canonical dispatchable state after a successful login.
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.RecordSuccessfulLogin("user@example.com", "My Org", "pro");
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialGate sut = new(dbContext, new FakeLoginSessionState(isActive: false));

        // Act
        bool result = await sut.CanDispatchAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenInvalid_ReturnsFalse()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.Invalidate("worker_auth_failed");
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialGate sut = new(dbContext, new FakeLoginSessionState(isActive: false));

        // Act
        bool result = await sut.CanDispatchAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenValidButLoginActive_ReturnsFalse()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialGate sut = new(dbContext, new FakeLoginSessionState(isActive: true));

        // Act
        bool result = await sut.CanDispatchAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenInvalidAndSpendBlocked_ReturnsFalse()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.Invalidate("worker_auth_failed");
            account.BlockSpend(DateTimeOffset.UtcNow.AddHours(1));
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialGate sut = new(dbContext, new FakeLoginSessionState(isActive: false));

        // Act
        bool result = await sut.CanDispatchAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenSpendBlocked_ReturnsFalse()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.BlockSpend(DateTimeOffset.UtcNow.AddHours(1));
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialGate sut = new(dbContext, new FakeLoginSessionState(isActive: false));

        // Act
        bool result = await sut.CanDispatchAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenNoAccountExists_ReturnsFalse()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialGate sut = new(dbContext, new FakeLoginSessionState(isActive: false));

        // Act
        bool result = await sut.CanDispatchAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenApiKeyModeWithUnreadableCredential_ReturnsFalse()
    {
        // Arrange — Unreadable is produced when EF cannot decrypt the stored api_key column.
        // Inject an undecryptable value via raw SQL so the gate truly sees an Unreadable credential
        // (SetAuthMode rejects Unreadable as a write-time guard — it would persist as NULL and
        // read back as NotConfigured, testing the wrong branch).
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
        CredentialGate sut = new(dbContext, new FakeLoginSessionState(isActive: false));

        // Act
        bool result = await sut.CanDispatchAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenApiKeyModeWithNotConfiguredCredential_ReturnsFalse()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.NotConfigured()));
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialGate sut = new(dbContext, new FakeLoginSessionState(isActive: false));

        // Act
        bool result = await sut.CanDispatchAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenApiKeyModeWithPresentCredential_ReturnsTrue()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.Present("sk-ant-test")));
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialGate sut = new(dbContext, new FakeLoginSessionState(isActive: false));

        // Act
        bool result = await sut.CanDispatchAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeTrue();
    }

    private sealed class FakeLoginSessionState(bool isActive) : ILoginSessionState
    {
        public bool IsLoginActive => isActive;
    }
}
