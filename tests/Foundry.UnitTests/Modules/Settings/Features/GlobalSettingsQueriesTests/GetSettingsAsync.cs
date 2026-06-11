using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsQueriesTests;

public sealed class GetSettingsAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetSettingsAsync()
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
        GlobalSettingsSummary? result = await sut.GetSettingsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WhenApiKeySettings_ReturnsApiKeyAuthMode()
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
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        GlobalSettingsSummary? result = await sut.GetSettingsAsync(TestContext.Current.CancellationToken);

        // Assert
        GlobalSettingsSummary summary = result.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.AuthMode.ShouldBe("ApiKey"),
            () => summary.AccessTokenPresent.ShouldBeFalse(),
            () => summary.RefreshTokenPresent.ShouldBeFalse(),
            () => summary.ExpiresAt.ShouldBeNull(),
            () => summary.SubscriptionType.ShouldBeNull());
    }

    [Fact]
    public async Task WhenOAuthSettings_ReturnsOAuthAuthMode()
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
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        GlobalSettingsSummary? result = await sut.GetSettingsAsync(TestContext.Current.CancellationToken);

        // Assert
        GlobalSettingsSummary summary = result.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.AuthMode.ShouldBe("OAuth"),
            () => summary.AccessTokenPresent.ShouldBeTrue(),
            () => summary.RefreshTokenPresent.ShouldBeTrue(),
            () => summary.ExpiresAt.ShouldBe(expiresAt),
            () => summary.SubscriptionType.ShouldBe("pro"));
    }

    [Fact]
    public async Task WhenSettings_ReturnsMaxConcurrentAndTimeout()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.UpdateLimits(7, 90);
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        GlobalSettingsSummary? result = await sut.GetSettingsAsync(TestContext.Current.CancellationToken);

        // Assert
        GlobalSettingsSummary summary = result.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.MaxConcurrent.ShouldBe(7),
            () => summary.TimeoutMinutes.ShouldBe(90));
    }
}
