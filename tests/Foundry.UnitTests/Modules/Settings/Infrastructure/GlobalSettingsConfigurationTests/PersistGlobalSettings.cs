using Foundry.Modules.Settings.Domain;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Infrastructure.GlobalSettingsConfigurationTests;

public sealed class PersistGlobalSettings : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistGlobalSettings()
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
    public async Task WhenApiKeySettingsPersisted_CanBeReloadedWithAllProperties()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        _dbContext.Set<GlobalSettings>().Add(settings);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        GlobalSettings? result = await _dbContext
            .Set<GlobalSettings>()
            .FindAsync([settings.Id], TestContext.Current.CancellationToken);

        // Assert
        GlobalSettings reloaded = result.ShouldNotBeNull();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.Id.ShouldBe(settings.Id),
            () => reloaded.AuthMode.ShouldBeOfType<AuthMode.ApiKey>(),
            () => reloaded.MaxConcurrent.ShouldBe(settings.MaxConcurrent),
            () => reloaded.TimeoutMinutes.ShouldBe(settings.TimeoutMinutes),
            () => reloaded.CreatedAt.ShouldBe(settings.CreatedAt),
            () => reloaded.UpdatedAt.ShouldBe(settings.UpdatedAt));
    }

    [Fact]
    public async Task WhenOAuthSettingsPersisted_CanBeReloadedWithSubscriptionType()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        AuthMode.OAuth oauthMode = new("pro");
        settings.SetAuthMode(oauthMode);

        _dbContext.Set<GlobalSettings>().Add(settings);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        GlobalSettings? result = await _dbContext
            .Set<GlobalSettings>()
            .FindAsync([settings.Id], TestContext.Current.CancellationToken);

        // Assert
        GlobalSettings reloaded = result.ShouldNotBeNull();
        AuthMode.OAuth reloadedOauth = reloaded.AuthMode.ShouldBeOfType<AuthMode.OAuth>();
        reloadedOauth.SubscriptionType.ShouldBe("pro");
    }

    [Fact]
    public async Task WhenDispatchPauseSettingsPersisted_CanBeReloadedWithAllProperties()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset resetsAt = new DateTimeOffset(
            DateTimeOffset.UtcNow.AddDays(3).Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond,
            TimeSpan.Zero);
        settings.SetUsageLimitResetsAt(resetsAt);
        settings.PauseDispatch();
        settings.UpdateDispatchSettings(autoResume: false, defaultCooldownMinutes: 90);

        _dbContext.Set<GlobalSettings>().Add(settings);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        GlobalSettings? result = await _dbContext
            .Set<GlobalSettings>()
            .FindAsync([settings.Id], TestContext.Current.CancellationToken);

        // Assert
        GlobalSettings reloaded = result.ShouldNotBeNull();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.UsageLimitResetsAt.ShouldBe(resetsAt),
            () => reloaded.IsDispatchPaused.ShouldBeTrue(),
            () => reloaded.AutoResumeOnUsageReset.ShouldBeFalse(),
            () => reloaded.DefaultCooldownMinutes.ShouldBe(90));
    }

    [Fact]
    public async Task WhenAuthInvalidPausePersisted_CanBeReloaded()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseForAuthInvalid();

        _dbContext.Set<GlobalSettings>().Add(settings);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        GlobalSettings? result = await _dbContext
            .Set<GlobalSettings>()
            .FindAsync([settings.Id], TestContext.Current.CancellationToken);

        // Assert
        GlobalSettings reloaded = result.ShouldNotBeNull();
        reloaded.AuthInvalidPause.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenApiKeySettingsPersisted_AuthModeColumnIsEncrypted()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.SetAuthMode(new AuthMode.ApiKey("my-plaintext-key"));

        _dbContext.Set<GlobalSettings>().Add(settings);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act — query raw column value to verify encryption
        await using Microsoft.Data.Sqlite.SqliteCommand command = _connection
            .CreateCommand();
        command.CommandText = "SELECT auth_mode FROM global_settings LIMIT 1";
        string? rawValue = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert — the raw stored value should not contain plaintext JSON
        string nonNull = rawValue.ShouldNotBeNull();
        nonNull.ShouldNotContain("api_key");
        nonNull.ShouldNotContain("my-plaintext-key");
    }
}
