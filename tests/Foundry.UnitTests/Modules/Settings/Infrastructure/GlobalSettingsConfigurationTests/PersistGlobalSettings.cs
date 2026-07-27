using Foundry.Modules.Settings.Domain.Entities;
using Foundry.WebApi.Persistence;

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

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenSettingsPersisted_CanBeReloadedWithAllProperties()
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
            () => reloaded.MaxConcurrent.ShouldBe(settings.MaxConcurrent),
            () => reloaded.TimeoutMinutes.ShouldBe(settings.TimeoutMinutes),
            () => reloaded.CreatedAt.ShouldBe(settings.CreatedAt),
            () => reloaded.UpdatedAt.ShouldBe(settings.UpdatedAt));
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
}
