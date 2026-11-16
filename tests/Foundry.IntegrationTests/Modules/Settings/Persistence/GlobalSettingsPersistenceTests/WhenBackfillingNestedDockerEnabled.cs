using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Migrations;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

/// <summary>
/// The AddNestedDockerEnabled migration sets the nested-Docker switch from the removed InstallDocker member still in
/// the stored worker_image_configuration JSON (ADR 0110). The factory uses EnsureCreated(), so migrations do not run;
/// the test seeds a pre-migration JSON shape and runs the migration's own backfill SQL.
/// </summary>
public sealed class WhenBackfillingNestedDockerEnabled : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenBackfillingNestedDockerEnabled()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenInstallDockerWasOn_TheSwitchIsOn()
    {
        // Arrange
        GlobalSettingsId id = await SeedPreMigrationRowAsync(
            """{"ImageTag":"foundry-worker:local","BuildEnabled":true,"InstallDotnet":true,"InstallDocker":true}""");

        // Act
        await ExecuteBackfillSqlAsync();

        // Assert
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);
        reloaded.ShouldNotBeNull().NestedDockerEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenInstallDockerWasOff_TheSwitchIsOff()
    {
        // Arrange
        GlobalSettingsId id = await SeedPreMigrationRowAsync(
            """{"ImageTag":"foundry-worker:local","BuildEnabled":true,"InstallDotnet":true,"InstallDocker":false}""");

        // Act
        await ExecuteBackfillSqlAsync();

        // Assert
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);
        reloaded.ShouldNotBeNull().NestedDockerEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenTheRowPredatesInstallDocker_TheSwitchIsOff()
    {
        // Arrange
        GlobalSettingsId id = await SeedPreMigrationRowAsync("""{"InstallDotnet":true,"InstallAngular":false}""");

        // Act
        await ExecuteBackfillSqlAsync();

        // Assert
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);
        reloaded.ShouldNotBeNull().NestedDockerEnabled.ShouldBeFalse();
    }

    private async Task<GlobalSettingsId> SeedPreMigrationRowAsync(string workerImageConfigurationJson)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE global_settings SET worker_image_configuration = {0}, nested_docker_enabled = 0",
            [workerImageConfigurationJson],
            TestContext.Current.CancellationToken);

        return settings.Id;
    }

    private async Task ExecuteBackfillSqlAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        // The SQL holds no format placeholders or braces, so ExecuteSqlRawAsync's formatting leaves it unchanged.
        await dbContext.Database.ExecuteSqlRawAsync(
            AddNestedDockerEnabled.BackfillFromInstallDockerSql,
            TestContext.Current.CancellationToken);
    }

    private async Task<GlobalSettings?> ReloadSettingsAsync(GlobalSettingsId id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        return await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, TestContext.Current.CancellationToken);
    }
}
