using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

public sealed class WhenStoredJsonHasOnlyOriginalTwoFlags : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenStoredJsonHasOnlyOriginalTwoFlags()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task InstallChromiumDefaultsToFalse()
    {
        // Arrange — write a row whose worker_image_configuration JSON contains only the original two keys
        // (InstallDotnet and InstallAngular), simulating a database row written before InstallChromium
        // and InstallDocker were added. InstallChromium is absent and must default to false.
        GlobalSettingsId id = await SeedSettingsWithTwoKeyJsonAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.InstallChromium.ShouldBeFalse();
    }

    [Fact]
    public async Task InstallDockerDefaultsToFalse()
    {
        // Arrange — write a row whose worker_image_configuration JSON contains only the original two keys
        // (InstallDotnet and InstallAngular), simulating a database row written before InstallChromium
        // and InstallDocker were added. InstallDocker is absent and must default to false.
        GlobalSettingsId id = await SeedSettingsWithTwoKeyJsonAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.InstallDocker.ShouldBeFalse();
    }

    [Fact]
    public async Task OriginalTwoFlagsDeserializeCorrectly()
    {
        // Arrange — write a row whose worker_image_configuration JSON contains only the original two keys.
        // The stored JSON has InstallDotnet=true and InstallAngular=false.
        GlobalSettingsId id = await SeedSettingsWithTwoKeyJsonAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.ShouldSatisfyAllConditions(
            () => reloaded.WorkerImageConfiguration.InstallDotnet.ShouldBeTrue(),
            () => reloaded.WorkerImageConfiguration.InstallAngular.ShouldBeFalse());
    }

    private async Task<GlobalSettingsId> SeedSettingsWithTwoKeyJsonAsync()
    {
        // Seed a normal row first so all other required columns are populated via EF,
        // then overwrite worker_image_configuration with a 2-key JSON string via raw SQL
        // to simulate a pre-migration row that does not contain InstallChromium or InstallDocker.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        const string twoKeyJson =
            """{"InstallDotnet":true,"InstallAngular":false}""";

        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE global_settings SET worker_image_configuration = {0}",
            [twoKeyJson],
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
