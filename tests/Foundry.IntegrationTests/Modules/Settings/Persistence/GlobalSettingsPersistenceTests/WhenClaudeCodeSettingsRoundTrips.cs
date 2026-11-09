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

    [Fact]
    public async Task LegacyJsonWithBlockCiCdEdits_LoadsAndIsDroppedOnNextSave()
    {
        // Arrange — a row written before the CI/CD edit-blocking setting was removed (ADR 0100)
        GlobalSettingsId id = await SeedAsync(GlobalSettings.Create());
        await WriteClaudeCodeSettingsJsonAsync("""{"BlockCiCdEdits":true,"AdditionalDenyRules":["Bash(rm:*)"]}""");
        ClaudeCodeSettings updated = ClaudeCodeSettings.Create(["Bash(rm:*)", "Bash(curl:*)"])
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

        // Act
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            GlobalSettings loaded = await dbContext.Set<GlobalSettings>()
                .SingleAsync(s => s.Id == id, TestContext.Current.CancellationToken);
            loaded.ClaudeCodeSettings.AdditionalDenyRules.ShouldBe(["Bash(rm:*)"]);
            loaded.UpdateClaudeCodeSettings(updated);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        string storedJson = await ReadClaudeCodeSettingsJsonAsync();
        storedJson.ShouldSatisfyAllConditions(
            () => storedJson.ShouldNotContain("BlockCiCdEdits"),
            () => storedJson.ShouldContain("Bash(curl:*)"));
    }

    private async Task WriteClaudeCodeSettingsJsonAsync(string json)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE global_settings SET claude_code_settings = {0}",
            [json],
            TestContext.Current.CancellationToken);
    }

    private async Task<string> ReadClaudeCodeSettingsJsonAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        return await dbContext.Database
            .SqlQueryRaw<string>("SELECT claude_code_settings AS Value FROM global_settings")
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
