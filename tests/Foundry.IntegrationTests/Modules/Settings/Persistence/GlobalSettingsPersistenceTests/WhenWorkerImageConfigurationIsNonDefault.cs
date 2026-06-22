using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

public sealed class WhenWorkerImageConfigurationIsNonDefault : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenWorkerImageConfigurationIsNonDefault()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WorkerImageConfigurationFlagsRoundTrip()
    {
        // Arrange
        WorkerImageConfiguration config = new(
            InstallDotnet: true,
            InstallAngular: true,
            InstallGlab: false,
            InstallGh: true);

        GlobalSettingsId id = await SeedSettingsWithConfigAsync(config);

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.ShouldSatisfyAllConditions(
            () => reloaded.WorkerImageConfiguration.InstallDotnet.ShouldBeTrue(),
            () => reloaded.WorkerImageConfiguration.InstallAngular.ShouldBeTrue(),
            () => reloaded.WorkerImageConfiguration.InstallGlab.ShouldBeFalse(),
            () => reloaded.WorkerImageConfiguration.InstallGh.ShouldBeTrue());
    }

    [Fact]
    public async Task ImageBuildStatusBuilding_RoundTrips()
    {
        // Arrange
        GlobalSettingsId id = await SeedSettingsWithImageBuildStatusAsync(ImageBuildStatus.Building);

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.ImageBuildStatus.ShouldBe(ImageBuildStatus.Building);
    }

    [Fact]
    public async Task ImageBuildStatusFailed_RoundTripsWithError()
    {
        // Arrange
        GlobalSettingsId id = await SeedSettingsWithFailedBuildAsync("build error output");

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.ImageBuildStatus.ShouldBe(ImageBuildStatus.Failed),
            () => reloaded.LastImageBuildError.ShouldBe("build error output"));
    }

    private async Task<GlobalSettingsId> SeedSettingsWithConfigAsync(WorkerImageConfiguration config)
    {
        // No endpoint exists to set these new properties — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdateWorkerImageConfiguration(config);
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return settings.Id;
    }

    private async Task<GlobalSettingsId> SeedSettingsWithImageBuildStatusAsync(ImageBuildStatus status)
    {
        // No endpoint exists to set build status — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        if (status == ImageBuildStatus.Building)
        {
            settings.BeginImageBuild();
        }

        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return settings.Id;
    }

    private async Task<GlobalSettingsId> SeedSettingsWithFailedBuildAsync(string errorTail)
    {
        // No endpoint exists to set failed build status — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        settings.FailImageBuild(errorTail);
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
