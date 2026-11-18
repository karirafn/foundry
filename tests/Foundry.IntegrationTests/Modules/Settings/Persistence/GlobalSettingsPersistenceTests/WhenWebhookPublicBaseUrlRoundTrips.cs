using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

public sealed class WhenWebhookPublicBaseUrlRoundTrips : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenWebhookPublicBaseUrlRoundTrips()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Saved_StoresTheNormalizedString()
    {
        // Arrange
        GlobalSettings seed = GlobalSettings.Create();
        seed.UpdateWebhookSettings(" https://hooks.example.com/foundry/ ", 15);

        // Act
        await SeedAsync(seed);

        // Assert
        string? stored = await ReadStoredUrlAsync();
        stored.ShouldBe("https://hooks.example.com/foundry");
    }

    [Fact]
    public async Task Saved_ReloadsAsTheSameValue()
    {
        // Arrange
        GlobalSettings seed = GlobalSettings.Create();
        seed.UpdateWebhookSettings("https://hooks.example.com", 15);
        GlobalSettingsId id = await SeedAsync(seed);

        // Act
        GlobalSettings reloaded = await ReloadAsync(id);

        // Assert
        reloaded.WebhookPublicBaseUrl.ShouldBe(seed.WebhookPublicBaseUrl);
    }

    [Fact]
    public async Task Saved_IsReadByTheWebhookSettingsQuery()
    {
        // Arrange
        GlobalSettings seed = GlobalSettings.Create();
        seed.UpdateWebhookSettings("https://hooks.example.com", 30);
        await SeedAsync(seed);

        // Act
        WebhookSettingsSnapshot snapshot = await QuerySnapshotAsync();

        // Assert
        snapshot.PublicBaseUrl.ShouldBe("https://hooks.example.com");
    }

    [Fact]
    public async Task Cleared_ReloadsAsNull()
    {
        // Arrange
        GlobalSettingsId id = await SeedAsync(GlobalSettings.Create());

        // Act
        GlobalSettings reloaded = await ReloadAsync(id);

        // Assert
        reloaded.WebhookPublicBaseUrl.ShouldBeNull();
    }

    [Fact]
    public async Task StoredBeforeNormalization_LoadsUnchanged()
    {
        // Arrange — a row written before the trailing slash was trimmed
        GlobalSettingsId id = await SeedAsync(GlobalSettings.Create());
        await WriteStoredUrlAsync("https://hooks.example.com/");

        // Act
        GlobalSettings reloaded = await ReloadAsync(id);

        // Assert
        reloaded.WebhookPublicBaseUrl.ShouldNotBeNull().Value.ShouldBe("https://hooks.example.com/");
    }

    private async Task<WebhookSettingsSnapshot> QuerySnapshotAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IWebhookSettingsQueries queries = scope.ServiceProvider.GetRequiredService<IWebhookSettingsQueries>();

        return await queries.GetWebhookSettingsAsync(TestContext.Current.CancellationToken);
    }

    private async Task WriteStoredUrlAsync(string url)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE global_settings SET webhook_public_base_url = {0}",
            [url],
            TestContext.Current.CancellationToken);
    }

    private async Task<string?> ReadStoredUrlAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        return await dbContext.Database
            .SqlQueryRaw<string?>("SELECT webhook_public_base_url AS Value FROM global_settings")
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task<GlobalSettingsId> SeedAsync(GlobalSettings settings)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return settings.Id;
    }

    private async Task<GlobalSettings> ReloadAsync(GlobalSettingsId id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        return await dbContext.Set<GlobalSettings>()
            .SingleAsync(s => s.Id == id, TestContext.Current.CancellationToken);
    }
}
