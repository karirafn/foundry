using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

/// <summary>
/// A persisted JSON blob that still carries removed members — the InstallGh / InstallGlab keys (#580) and the four
/// toolchain install flags (ADR 0109) — must deserialize without error and bind the surviving ImageTag and
/// BuildEnabled. Default STJ unknown-property handling silently drops unknown keys.
/// </summary>
public sealed class WhenStoredJsonContainsRemovedInstallFlags : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenStoredJsonContainsRemovedInstallFlags()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task DeserializesWithoutError()
    {
        // Arrange — seed a JSON blob that still contains the removed InstallGh / InstallGlab keys,
        // simulating a row written by an older version of the application before #580 was deployed.
        GlobalSettingsId id = await SeedSettingsWithLegacyJsonAsync();

        // Act — reload; should not throw
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
    }

    [Fact]
    public async Task ImageTagBindsCorrectly()
    {
        // Arrange
        GlobalSettingsId id = await SeedSettingsWithLegacyJsonAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.ImageTag.ShouldBe("foundry-worker:v7");
    }

    [Fact]
    public async Task BuildEnabledBindsCorrectly()
    {
        // Arrange
        GlobalSettingsId id = await SeedSettingsWithLegacyJsonAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert — the removed keys must not cross-wire to the surviving flag
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.BuildEnabled.ShouldBeFalse();
    }

    private async Task<GlobalSettingsId> SeedSettingsWithLegacyJsonAsync()
    {
        // Seed a normal row first so all other required columns are populated via EF,
        // then overwrite worker_image_configuration with a legacy JSON blob via raw SQL
        // to simulate a row written before the install flags were removed.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Legacy blob: contains every removed install key alongside the survivors.
        const string legacyJson =
            """{"ImageTag":"foundry-worker:v7","BuildEnabled":false,"InstallDotnet":true,"InstallAngular":true,"InstallGlab":true,"InstallGh":true,"InstallChromium":true,"InstallDocker":true}""";

        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE global_settings SET worker_image_configuration = {0}",
            [legacyJson],
            TestContext.Current.CancellationToken);

        return settings.Id;
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
