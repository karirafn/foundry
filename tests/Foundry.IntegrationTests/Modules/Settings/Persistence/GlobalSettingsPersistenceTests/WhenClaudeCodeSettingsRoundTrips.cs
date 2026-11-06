using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

public sealed class WhenClaudeCodeSettingsRoundTrips : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenClaudeCodeSettingsRoundTrips()
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
        reloaded.ClaudeCodeSettings.ShouldBe(ClaudeCodeSettings.Default);
    }

    [Fact]
    public async Task NonDefaultValues_RoundTrip()
    {
        // Arrange
        ClaudeCodeSettings settings = ClaudeCodeSettings.Create(
                false,
                ["Bash(rm:*)", "Read(./secrets/**)"])
            .Match(v => v, e => throw new InvalidOperationException(e.Message));
        GlobalSettings seed = GlobalSettings.Create();
        seed.UpdateClaudeCodeSettings(settings);
        GlobalSettingsId id = await SeedAsync(seed);

        // Act
        GlobalSettings? reloaded = await ReloadAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.ClaudeCodeSettings.ShouldBe(settings);
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
