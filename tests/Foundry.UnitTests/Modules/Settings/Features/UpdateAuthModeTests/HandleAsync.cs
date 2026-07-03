using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.UpdateAuthModeTests;

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
            GlobalSettings settings = GlobalSettings.Create();
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = new(dbContext);

        UpdateAuthMode.Command command = new("api_key", "sk-ant-abc123");

        // Act
        Result<UpdateAuthMode.Response> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<UpdateAuthMode.Response>.Success success =
            result.ShouldBeOfType<Result<UpdateAuthMode.Response>.Success>();
        success.Value.AuthMode.ShouldBe("ApiKey");
    }

    [Fact]
    public async Task WhenSwitchingToOAuthMode_SetsConfiguredMarkerWithNoScan()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.SetAuthMode(new AuthMode.ApiKey("old-key"));
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = new(dbContext);

        UpdateAuthMode.Command command = new("oauth", null);

        // Act
        Result<UpdateAuthMode.Response> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<UpdateAuthMode.Response>.Success success =
            result.ShouldBeOfType<Result<UpdateAuthMode.Response>.Success>();
        success.Value.AuthMode.ShouldBe("OAuth");
    }

    [Fact]
    public async Task WhenSwitchingToOAuthMode_PersistsOAuthMarkerWithoutSubscriptionType()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            seedDb.Set<GlobalSettings>().Add(settings);
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
        GlobalSettings persisted = (await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken))!;
        AuthMode.OAuth oauth = persisted.AuthMode.ShouldBeOfType<AuthMode.OAuth>();
        oauth.SubscriptionType.ShouldBeNull();
    }

    [Fact]
    public async Task WhenSettingsNotFound_ReturnsNotFoundError()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = new(dbContext);

        UpdateAuthMode.Command command = new("api_key", "sk-ant-abc123");

        // Act
        Result<UpdateAuthMode.Response> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<UpdateAuthMode.Response>.Failure failure =
            result.ShouldBeOfType<Result<UpdateAuthMode.Response>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.NotFoundCode);
    }

    [Fact]
    public async Task WhenSwitchingToApiKeyMode_PersistsChangesToDatabase()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings seed = GlobalSettings.Create();
            seed.SetAuthMode(new AuthMode.OAuth(SubscriptionType: null));
            seedDb.Set<GlobalSettings>().Add(seed);
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
        GlobalSettings settings = (await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken))!;
        settings.AuthMode.ShouldBeOfType<AuthMode.ApiKey>();
    }
}
