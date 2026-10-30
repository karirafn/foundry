using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

public sealed class WhenWorkerPromptsRoundTrip : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenWorkerPromptsRoundTrip()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WorkerPromptsRoundTripsWithDefaultValues()
    {
        // Arrange
        GlobalSettingsId id = await SeedDefaultSettingsAsync();

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerPrompts.ShouldBe(WorkerPrompts.Default);
    }

    [Fact]
    public async Task WhenCustomPromptsSet_SystemPromptTemplateRoundTrips()
    {
        // Arrange
        const string customSystem = "Custom system prompt for issue #{issueNumber}.";
        GlobalSettingsId id = await SeedSettingsWithCustomPromptsAsync(customSystem, "Worker prompt #{issueNumber}.", "Use feat/ prefix");

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerPrompts.SystemPromptTemplate.ShouldBe(customSystem);
    }

    [Fact]
    public async Task WhenCustomPromptsSet_WorkerPromptTemplateRoundTrips()
    {
        // Arrange
        const string customWorker = "Custom worker prompt for #{issueNumber}.";
        GlobalSettingsId id = await SeedSettingsWithCustomPromptsAsync("Custom system prompt.", customWorker, "Use feat/ prefix");

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerPrompts.WorkerPromptTemplate.ShouldBe(customWorker);
    }

    [Fact]
    public async Task WhenCustomPromptsSet_BranchNamingInstructionRoundTrips()
    {
        // Arrange
        const string customBranchNaming = "Use feat/TICKET-ID-description format";
        GlobalSettingsId id = await SeedSettingsWithCustomPromptsAsync("Custom system prompt.", "Custom worker #{issueNumber}.", customBranchNaming);

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerPrompts.BranchNamingInstruction.ShouldBe(customBranchNaming);
    }

    private async Task<GlobalSettingsId> SeedDefaultSettingsAsync()
    {
        // No endpoint exists to set WorkerPrompts — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return settings.Id;
    }

    private async Task<GlobalSettingsId> SeedSettingsWithCustomPromptsAsync(
        string systemPromptTemplate,
        string workerPromptTemplate,
        string branchNamingInstruction)
    {
        // No endpoint exists to set WorkerPrompts — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        WorkerPrompts prompts = WorkerPrompts.Create(
                systemPromptTemplate,
                workerPromptTemplate,
                branchNamingInstruction)
            .Match(onSuccess: v => v, onFailure: e => throw new InvalidOperationException(e.Message));

        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdatePrompts(prompts);
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
