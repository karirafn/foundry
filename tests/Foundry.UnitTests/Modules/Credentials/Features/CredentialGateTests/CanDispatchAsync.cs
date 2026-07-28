using Foundry.Modules.Credentials.Domain.Entities;
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
    public async Task WhenValidAndLoginNotActive_ReturnsTrue()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
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

    private sealed class FakeLoginSessionState(bool isActive) : ILoginSessionState
    {
        public bool IsLoginActive => isActive;
    }
}
