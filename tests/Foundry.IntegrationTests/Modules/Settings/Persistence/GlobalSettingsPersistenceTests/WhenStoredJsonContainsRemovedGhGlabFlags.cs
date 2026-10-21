using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

/// <summary>
/// AC3 regression: a persisted JSON blob that still contains the removed InstallGh / InstallGlab keys
/// (written by an older version of the application) must deserialize without error and bind the four
/// surviving flags correctly. Default STJ unknown-property handling silently drops unknown keys.
/// </summary>
public sealed class WhenStoredJsonContainsRemovedGhGlabFlags : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenStoredJsonContainsRemovedGhGlabFlags()
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
    public async Task InstallDotnetBindsCorrectly()
    {
        // Arrange
        GlobalSettingsId id = await SeedSettingsWithLegacyJsonAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert — legacy JSON has InstallDotnet=true; it must bind to the surviving flag
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.InstallDotnet.ShouldBeTrue();
    }

    [Fact]
    public async Task InstallAngularBindsCorrectly()
    {
        // Arrange
        GlobalSettingsId id = await SeedSettingsWithLegacyJsonAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert — legacy JSON has InstallAngular=false
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.InstallAngular.ShouldBeFalse();
    }

    [Fact]
    public async Task InstallChromiumBindsCorrectly()
    {
        // Arrange
        GlobalSettingsId id = await SeedSettingsWithLegacyJsonAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert — legacy JSON has InstallChromium=false; the removed keys must not cross-wire to this flag
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.InstallChromium.ShouldBeFalse();
    }

    [Fact]
    public async Task InstallDockerBindsCorrectly()
    {
        // Arrange
        GlobalSettingsId id = await SeedSettingsWithLegacyJsonAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert — legacy JSON has InstallDocker=true; it must bind to the surviving flag
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.InstallDocker.ShouldBeTrue();
    }

    private async Task<GlobalSettingsId> SeedSettingsWithLegacyJsonAsync()
    {
        // Seed a normal row first so all other required columns are populated via EF,
        // then overwrite worker_image_configuration with a legacy 6-key JSON blob via raw SQL
        // to simulate a row written before InstallGh / InstallGlab were removed.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Legacy blob: contains the now-removed InstallGlab and InstallGh keys alongside the survivors.
        const string legacyJson =
            """{"InstallDotnet":true,"InstallAngular":false,"InstallGlab":true,"InstallGh":false,"InstallChromium":false,"InstallDocker":true}""";

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
