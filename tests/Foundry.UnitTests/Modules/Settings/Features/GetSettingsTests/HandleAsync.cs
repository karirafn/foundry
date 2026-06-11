using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GetSettingsTests;

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
    public async Task WhenSettingsExistWithApiKeyMode_ReturnsSettingsSummary()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.SetAuthMode(new AuthMode.ApiKey("encrypted-key"));
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GetSettings.Handler sut = new(dbContext);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            new GetSettings.Query(),
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Success success = result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.AuthMode.ShouldBe("ApiKey"),
            () => success.Value.MaxConcurrent.ShouldBe(GlobalSettings.DefaultMaxConcurrent),
            () => success.Value.TimeoutMinutes.ShouldBe(GlobalSettings.DefaultTimeoutMinutes),
            () => success.Value.AccessTokenPresent.ShouldBeFalse(),
            () => success.Value.RefreshTokenPresent.ShouldBeFalse(),
            () => success.Value.ExpiresAt.ShouldBeNull(),
            () => success.Value.SubscriptionType.ShouldBeNull());
    }

    [Fact]
    public async Task WhenSettingsExistWithOAuthMode_ReturnsSettingsSummaryWithOAuthDetails()
    {
        // Arrange
        DateTimeOffset expiresAt = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.SetAuthMode(new AuthMode.OAuth("access-token", "refresh-token", expiresAt, "pro"));
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GetSettings.Handler sut = new(dbContext);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            new GetSettings.Query(),
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Success success = result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.AuthMode.ShouldBe("OAuth"),
            () => success.Value.AccessTokenPresent.ShouldBeTrue(),
            () => success.Value.RefreshTokenPresent.ShouldBeTrue(),
            () => success.Value.ExpiresAt.ShouldBe(expiresAt),
            () => success.Value.SubscriptionType.ShouldBe("pro"));
    }

    [Fact]
    public async Task WhenNoSettingsExist_ReturnsNotFoundError()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        GetSettings.Handler sut = new(dbContext);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            new GetSettings.Query(),
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Failure failure = result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.NotFoundCode);
    }
}
