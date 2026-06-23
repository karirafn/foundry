using Foundry.Modules.Settings.Domain;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

public sealed class WhenStoredJsonHasOnlyOriginalFourFlags : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenStoredJsonHasOnlyOriginalFourFlags()
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
        // Arrange — write a row whose worker_image_configuration JSON contains only the original four keys,
        // simulating a database row written before InstallChromium and InstallDocker were added.
        GlobalSettingsId id = await SeedSettingsWithFourKeyJsonAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.InstallChromium.ShouldBeFalse();
    }

    [Fact]
    public async Task InstallDockerDefaultsToFalse()
    {
        // Arrange — write a row whose worker_image_configuration JSON contains only the original four keys,
        // simulating a database row written before InstallChromium and InstallDocker were added.
        GlobalSettingsId id = await SeedSettingsWithFourKeyJsonAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.InstallDocker.ShouldBeFalse();
    }

    [Fact]
    public async Task OriginalFourFlagsDeserializeCorrectly()
    {
        // Arrange — write a row whose worker_image_configuration JSON contains only the original four keys.
        // The stored JSON has InstallDotnet=true and the rest false.
        GlobalSettingsId id = await SeedSettingsWithFourKeyJsonAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.ShouldSatisfyAllConditions(
            () => reloaded.WorkerImageConfiguration.InstallDotnet.ShouldBeTrue(),
            () => reloaded.WorkerImageConfiguration.InstallAngular.ShouldBeFalse(),
            () => reloaded.WorkerImageConfiguration.InstallGlab.ShouldBeFalse(),
            () => reloaded.WorkerImageConfiguration.InstallGh.ShouldBeFalse());
    }

    private async Task<GlobalSettingsId> SeedSettingsWithFourKeyJsonAsync()
    {
        // Seed a normal row first so all other required columns are populated via EF,
        // then overwrite worker_image_configuration with a 4-key JSON string via raw SQL
        // to simulate a pre-migration row that does not contain InstallChromium or InstallDocker.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        const string fourKeyJson =
            """{"InstallDotnet":true,"InstallAngular":false,"InstallGlab":false,"InstallGh":false}""";

        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE global_settings SET worker_image_configuration = {0}",
            [fourKeyJson],
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
