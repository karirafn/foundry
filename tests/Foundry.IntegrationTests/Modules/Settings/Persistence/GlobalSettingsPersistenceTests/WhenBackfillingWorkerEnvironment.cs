using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Migrations;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

/// <summary>
/// Verifies the frozen default JSON used by the AddWorkerResourceLimitsAndMounts migration
/// matches the converters' serialized shape and loads as the current defaults. The factory uses
/// EnsureCreated() so migrations do not run in tests — the column default the migration applies
/// to pre-existing rows is simulated by writing the same literal directly.
/// </summary>
public sealed class WhenBackfillingWorkerEnvironment : IAsyncDisposable
{
    private const string FrozenLimitsJson = AddWorkerResourceLimitsAndMounts.DefaultWorkerResourceLimitsJson;
    private const string FrozenMountsJson = AddWorkerResourceLimitsAndMounts.DefaultWorkerMountsJson;

    private readonly FoundryWebAppFactory _factory;

    public WhenBackfillingWorkerEnvironment()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task PreMigrationRow_BackfillsToDefaultLimitsAndMounts()
    {
        // Arrange
        GlobalSettingsId id = await SeedRowWithRawColumnsAsync(FrozenLimitsJson, FrozenMountsJson);

        // Act
        GlobalSettings? reloaded = await ReloadAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerResourceLimits.ShouldBe(WorkerResourceLimits.Default),
            () => reloaded.WorkerMounts.ShouldBe(WorkerMounts.Default));
    }

    [Fact]
    public async Task ConverterOutputForDefaults_EqualsFrozenMigrationJson()
    {
        // Arrange
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        GlobalSettings fresh = GlobalSettings.Create();
        dbContext.Set<GlobalSettings>().Add(fresh);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        List<string> limitsJson = await dbContext.Database
            .SqlQueryRaw<string>(
                "SELECT worker_resource_limits AS Value FROM global_settings WHERE id = {0}",
                fresh.Id.Value)
            .ToListAsync(TestContext.Current.CancellationToken);
        List<string> mountsJson = await dbContext.Database
            .SqlQueryRaw<string>(
                "SELECT worker_mounts AS Value FROM global_settings WHERE id = {0}",
                fresh.Id.Value)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        limitsJson.ShouldBe([FrozenLimitsJson]);
        mountsJson.ShouldBe([FrozenMountsJson]);
    }

    private async Task<GlobalSettingsId> SeedRowWithRawColumnsAsync(string limitsJson, string mountsJson)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE global_settings SET worker_resource_limits = {0}, worker_mounts = {1} WHERE id = {2}",
            [limitsJson, mountsJson, settings.Id.Value],
            TestContext.Current.CancellationToken);

        return settings.Id;
    }

    private async Task<GlobalSettings?> ReloadAsync(GlobalSettingsId id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        return await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.Id == id,
                TestContext.Current.CancellationToken);
    }
}
