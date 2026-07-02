using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.Modules.Settings.Infrastructure;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.UpdateAuthModeTests;

internal sealed class FakeOAuthCredentialScanner(Result<OAuthCredentials> result) : IOAuthCredentialScanner
{
    public Task<Result<OAuthCredentials>> ScanAsync(CancellationToken cancellationToken) =>
        Task.FromResult(result);
}

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

    private static FakeOAuthCredentialScanner SuccessfulScanner(OAuthCredentials credentials) =>
        new(Result<OAuthCredentials>.Ok(credentials));

    private static FakeOAuthCredentialScanner FailingScanner() =>
        new(Result<OAuthCredentials>.Fail(SettingsErrors.OAuthCredentialsNotFound));

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
        UpdateAuthMode.Handler sut = new(dbContext, SuccessfulScanner(
            new OAuthCredentials("at", "rt", DateTimeOffset.UtcNow.AddHours(1), "pro")));

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
    public async Task WhenSwitchingToOAuthMode_ScansCredentialsAndUpdatesAuthMode()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.SetAuthMode(new AuthMode.ApiKey("old-key"));
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        OAuthCredentials credentials = new("my-access", "my-refresh", DateTimeOffset.UtcNow.AddHours(1), "max");
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = new(dbContext, SuccessfulScanner(credentials));

        UpdateAuthMode.Command command = new("oauth", null);

        // Act
        Result<UpdateAuthMode.Response> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<UpdateAuthMode.Response>.Success success =
            result.ShouldBeOfType<Result<UpdateAuthMode.Response>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.AuthMode.ShouldBe("OAuth"),
            () => success.Value.AccessTokenPresent.ShouldBeFalse(),
            () => success.Value.RefreshTokenPresent.ShouldBeFalse(),
            () => success.Value.ExpiresAt.ShouldBeNull(),
            () => success.Value.SubscriptionType.ShouldBe("max"));
    }

    [Fact]
    public async Task WhenSwitchingToOAuthModeButScanFails_ReturnsOAuthCredentialsNotFoundError()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = new(dbContext, FailingScanner());

        UpdateAuthMode.Command command = new("oauth", null);

        // Act
        Result<UpdateAuthMode.Response> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<UpdateAuthMode.Response>.Failure failure =
            result.ShouldBeOfType<Result<UpdateAuthMode.Response>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.OAuthCredentialsNotFoundCode);
    }

    [Fact]
    public async Task WhenSettingsNotFound_ReturnsNotFoundError()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = new(dbContext, FailingScanner());

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
            seed.SetAuthMode(new AuthMode.OAuth("pro"));
            seedDb.Set<GlobalSettings>().Add(seed);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateAuthMode.Handler sut = new(dbContext, FailingScanner());
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
