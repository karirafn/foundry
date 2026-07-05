using Foundry.Modules.Credentials.Domain;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.CredentialQueriesTests;

public sealed class GetAuthEnvironmentVariableAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetAuthEnvironmentVariableAsync()
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
        (string Key, string Value)? result = await sut.GetAuthEnvironmentVariableAsync(
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WhenApiKeyMode_ReturnsAnthropicApiKeyVariable()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.SetAuthMode(new AuthMode.ApiKey("my-api-key"));
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CredentialQueries sut = new(dbContext);

        // Act
        (string Key, string Value)? result = await sut.GetAuthEnvironmentVariableAsync(
            TestContext.Current.CancellationToken);

        // Assert
        (string Key, string Value) pair = result.ShouldNotBeNull();
        pair.ShouldSatisfyAllConditions(
            () => pair.Key.ShouldBe("ANTHROPIC_API_KEY"),
            () => pair.Value.ShouldBe("my-api-key"));
    }

    [Fact]
    public async Task WhenOAuthMode_ReturnsNull()
    {
        // Arrange — OAuth credentials are sourced from the shared volume; no env var is injected.
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
        (string Key, string Value)? result = await sut.GetAuthEnvironmentVariableAsync(
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }
}
