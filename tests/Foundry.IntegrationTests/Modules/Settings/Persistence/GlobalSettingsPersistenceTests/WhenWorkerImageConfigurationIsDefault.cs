using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

public sealed class WhenWorkerImageConfigurationIsDefault : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenWorkerImageConfigurationIsDefault()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WorkerImageConfigurationRoundTripsWithDefaultValues()
    {
        // Arrange
        GlobalSettingsId id = await SeedDefaultSettingsAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.ShouldBe(WorkerImageConfiguration.Default);
    }

    [Fact]
    public async Task WhenDefaultSettings_InstallChromiumIsFalse()
    {
        // Arrange
        GlobalSettingsId id = await SeedDefaultSettingsAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.InstallChromium.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenDefaultSettings_InstallDockerIsFalse()
    {
        // Arrange
        GlobalSettingsId id = await SeedDefaultSettingsAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.InstallDocker.ShouldBeFalse();
    }

    [Fact]
    public async Task ImageBuildStateRoundTripsAsIdle()
    {
        // Arrange
        GlobalSettingsId id = await SeedDefaultSettingsAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.ImageBuildState.ShouldBeOfType<ImageBuildState.Idle>();
    }

    private async Task<GlobalSettingsId> SeedDefaultSettingsAsync()
    {
        // No endpoint exists to set these new properties — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

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
