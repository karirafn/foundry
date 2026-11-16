using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

/// <summary>
/// A row written before ImageTag and BuildEnabled existed holds only the original install keys; it loads as the default
/// configuration.
/// </summary>
public sealed class WhenStoredJsonPredatesTheImageTag : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenStoredJsonPredatesTheImageTag()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task LoadsAsTheDefaultConfiguration()
    {
        // Arrange
        GlobalSettingsId id = await SeedSettingsWithTwoKeyJsonAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.ShouldBe(WorkerImageConfiguration.Default);
    }

    private async Task<GlobalSettingsId> SeedSettingsWithTwoKeyJsonAsync()
    {
        // Seed a normal row first so all other required columns are populated via EF,
        // then overwrite worker_image_configuration with a 2-key JSON string via raw SQL
        // to simulate a row written before ImageTag and BuildEnabled were added.
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
