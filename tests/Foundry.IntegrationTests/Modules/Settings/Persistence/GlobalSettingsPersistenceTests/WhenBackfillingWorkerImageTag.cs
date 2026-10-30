using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

/// <summary>
/// Verifies that the worker_image_configuration JSON column deserializes correctly after the
/// AddWorkerImageTagAndBuildToggle migration's backfill runs. The factory uses EnsureCreated()
/// so migrations do not run in tests — the backfill output is simulated by executing the same
/// json_set SQL directly, exactly as WhenBackfillingLastImageBuiltAt does for its backfill.
/// This proves the deserializer survives the JSON shape produced by json_set().
/// </summary>
public sealed class WhenBackfillingWorkerImageTag : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenBackfillingWorkerImageTag()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task BackfilledRow_ImageTagIsFoundryWorkerLocal()
    {
        // Arrange — seed a row written before the ImageTag/BuildEnabled fields were added.
        // Strip those keys via json_remove to simulate a pre-migration row, then run the Up() SQL.
        GlobalSettingsId id = await SeedPreMigrationRowAsync();

        // Act — execute the same backfill SQL used in migration AddWorkerImageTagAndBuildToggle.
        await ExecuteBackfillSqlAsync();

        // Assert
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.ImageTag.ShouldBe("foundry-worker:local");
    }

    [Fact]
    public async Task BackfilledRow_BuildEnabledIsTrue()
    {
        // Arrange — seed a row written before the ImageTag/BuildEnabled fields were added.
        GlobalSettingsId id = await SeedPreMigrationRowAsync();

        // Act — execute the same backfill SQL used in migration AddWorkerImageTagAndBuildToggle.
        await ExecuteBackfillSqlAsync();

        // Assert
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.BuildEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task BackfilledRow_ExistingFlagsArePreserved()
    {
        // Arrange — seed a row with non-default install flags written before the backfill migration.
        GlobalSettingsId id = await SeedPreMigrationRowWithFlagsAsync(installDotnet: true, installAngular: true);

        // Act — run the backfill. It must not overwrite the existing install flags.
        await ExecuteBackfillSqlAsync();

        // Assert — install flags survive the json_set that adds ImageTag and BuildEnabled.
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);
        reloaded.ShouldNotBeNull();
        reloaded.WorkerImageConfiguration.ShouldSatisfyAllConditions(
            () => reloaded.WorkerImageConfiguration.InstallDotnet.ShouldBeTrue(),
            () => reloaded.WorkerImageConfiguration.InstallAngular.ShouldBeTrue());
    }

    private async Task<GlobalSettingsId> SeedPreMigrationRowAsync()
    {
        return await SeedPreMigrationRowWithFlagsAsync(installDotnet: false, installAngular: false);
    }

    private async Task<GlobalSettingsId> SeedPreMigrationRowWithFlagsAsync(bool installDotnet, bool installAngular)
    {
        // Seed a normal row via EF, then strip the ImageTag and BuildEnabled keys from the JSON
        // blob via raw SQL to simulate a row written before the AddWorkerImageTagAndBuildToggle
        // migration. ExecuteSqlRawAsync's {0}/{1} placeholders avoid format-string collisions
        // with JSON braces.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        WorkerImageConfiguration config = WorkerImageConfiguration.Create(
                imageTag: WorkerImageConfiguration.Default.ImageTag,
                buildEnabled: WorkerImageConfiguration.Default.BuildEnabled,
                installDotnet: installDotnet,
                installAngular: installAngular,
                installChromium: false,
                installDocker: false)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdateWorkerImageConfiguration(config);
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Strip the new keys to simulate the pre-migration JSON shape.
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE global_settings
            SET worker_image_configuration = json_remove(
                worker_image_configuration,
                '$.ImageTag',
                '$.BuildEnabled'
            )
            WHERE id = {0}
            """,
            [settings.Id.Value],
            TestContext.Current.CancellationToken);

        return settings.Id;
    }

    private async Task ExecuteBackfillSqlAsync()
    {
        // Run the same SQL as the Up() method in migration AddWorkerImageTagAndBuildToggle.
        // The factory uses EnsureCreated() so migrations do not run — execute the backfill directly.
        // Literal braces in the JSON value must be doubled so ExecuteSqlRawAsync's internal
        // String.Format call treats them as literal characters, not format placeholders.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE global_settings
            SET worker_image_configuration = json_set(
                worker_image_configuration,
                '$.ImageTag',    'foundry-worker:local',
                '$.BuildEnabled', json('true')
            )
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
