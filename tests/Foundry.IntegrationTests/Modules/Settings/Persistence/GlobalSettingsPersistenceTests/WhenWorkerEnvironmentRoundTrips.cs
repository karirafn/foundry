using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

public sealed class WhenWorkerEnvironmentRoundTrips : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenWorkerEnvironmentRoundTrips()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task DefaultValues_RoundTrip()
    {
        // Arrange
        GlobalSettingsId id = await SeedAsync(GlobalSettings.Create());

        // Act
        GlobalSettings? reloaded = await ReloadAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerResourceLimits.ShouldBe(WorkerResourceLimits.Default),
            () => reloaded.WorkerMounts.ShouldBe(WorkerMounts.Default));
    }

    [Fact]
    public async Task NonDefaultValues_RoundTrip()
    {
        // Arrange
        WorkerResourceLimits limits = WorkerResourceLimits.Create(4096, 1.5, 256)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));
        WorkerMounts mounts = WorkerMounts.Create(
                [new WorkerMount("/data", "/srv/data", true), new WorkerMount("/cache", "/srv/cache", false)])
            .Match(v => v, e => throw new InvalidOperationException(e.Message));
        GlobalSettings seed = GlobalSettings.Create();
        seed.UpdateWorkerEnvironment(limits, mounts);
        GlobalSettingsId id = await SeedAsync(seed);

        // Act
        GlobalSettings? reloaded = await ReloadAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerResourceLimits.ShouldBe(limits),
            () => reloaded.WorkerMounts.ShouldBe(mounts));
    }

    [Fact]
    public async Task MountsChange_IsDetectedAndPersisted()
    {
        // Arrange
        GlobalSettingsId id = await SeedAsync(GlobalSettings.Create());
        WorkerMounts mounts = WorkerMounts.Create([new WorkerMount("/data", "/srv/data", false)])
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            GlobalSettings tracked = await dbContext.Set<GlobalSettings>()
                .FirstAsync(s => s.Id == id, TestContext.Current.CancellationToken);
            tracked.UpdateWorkerEnvironment(WorkerResourceLimits.Default, mounts);

            // Act
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        GlobalSettings? reloaded = await ReloadAsync(id);
        reloaded.ShouldNotBeNull();
        reloaded.WorkerMounts.ShouldBe(mounts);
    }

    private async Task<GlobalSettingsId> SeedAsync(GlobalSettings settings)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return settings.Id;
    }

    private async Task<GlobalSettings?> ReloadAsync(GlobalSettingsId id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        return await dbContext.Set<GlobalSettings>()
            .FirstOrDefaultAsync(
                s => s.Id == id,
                TestContext.Current.CancellationToken);
    }
}
