using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsQueriesTests;

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
    public async Task WhenNoSettingsExist_ReturnsNull()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

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
            GlobalSettings settings = GlobalSettings.Create();
            settings.SetAuthMode(new AuthMode.ApiKey("my-api-key"));
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

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
    public async Task WhenOAuthMode_ReturnsAnthropicAuthTokenVariable()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.SetAuthMode(
                new AuthMode.OAuth("my-access-token", "my-refresh-token", DateTimeOffset.UtcNow, "pro"));
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        (string Key, string Value)? result = await sut.GetAuthEnvironmentVariableAsync(
            TestContext.Current.CancellationToken);

        // Assert
        (string Key, string Value) pair = result.ShouldNotBeNull();
        pair.ShouldSatisfyAllConditions(
            () => pair.Key.ShouldBe("ANTHROPIC_AUTH_TOKEN"),
            () => pair.Value.ShouldBe("my-access-token"));
    }
}
