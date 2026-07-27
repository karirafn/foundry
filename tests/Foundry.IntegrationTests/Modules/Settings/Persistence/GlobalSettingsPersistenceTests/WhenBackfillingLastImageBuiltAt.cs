using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

public sealed class WhenBackfillingLastImageBuiltAt : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenBackfillingLastImageBuiltAt()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task BackfilledLastImageBuiltAt_HasValue()
    {
        // Arrange — seed an idle row with null last_image_built_at to simulate pre-migration state.
        GlobalSettingsId id = await SeedIdleSettingsWithNullLastImageBuiltAtAsync();

        // Act — execute the same backfill SQL used in migration 20260623194945_AddLastImageBuiltAt.
        await ExecuteBackfillSqlAsync();

        // Assert
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);
        reloaded.ShouldNotBeNull();
        reloaded.LastImageBuiltAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task BackfilledLastImageBuiltAt_RoundTripsAsUtc()
    {
        // Arrange — seed an idle row with null last_image_built_at to simulate pre-migration state.
        GlobalSettingsId id = await SeedIdleSettingsWithNullLastImageBuiltAtAsync();

        // Act — execute the same backfill SQL used in migration 20260623194945_AddLastImageBuiltAt.
        await ExecuteBackfillSqlAsync();

        // Assert
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);
        reloaded.ShouldNotBeNull();
        DateTimeOffset lastImageBuiltAt = reloaded.LastImageBuiltAt.ShouldNotBeNull();
        lastImageBuiltAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    private async Task<GlobalSettingsId> SeedIdleSettingsWithNullLastImageBuiltAtAsync()
    {
        // Seed via DbContext — GlobalSettings.Create() starts in Idle state with LastImageBuiltAt = null.
        // No HTTP endpoint can set these fields, so direct DbContext seeding is required.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return settings.Id;
    }

    private async Task ExecuteBackfillSqlAsync()
    {
        // Run the same SQL as the Up() method in migration 20260623194945_AddLastImageBuiltAt.
        // The factory uses EnsureCreated() so migrations do not run — execute the backfill directly.
        // Raw string literal without C# interpolation: avoid the brace-collision between JSON
        // and ExecuteSqlRawAsync's parameter-placeholder scanning.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        // Literal braces in the JSON value must be doubled so ExecuteSqlRawAsync's internal
        // String.Format call treats them as literal characters, not format placeholders.
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE global_settings
            SET last_image_built_at = strftime('%Y-%m-%d %H:%M:%S', 'now') || '.0000000+00:00'
            WHERE image_build_status = '{{"type":"idle"}}';
            """,
            TestContext.Current.CancellationToken);
    }

    private async Task<GlobalSettings?> ReloadSettingsAsync(GlobalSettingsId id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        return await dbContext.Set<GlobalSettings>()
            .FirstOrDefaultAsync(
                s => s.Id == id,
                TestContext.Current.CancellationToken);
    }
}
