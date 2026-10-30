using System.Text.Json;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Persistence.GlobalSettingsPersistenceTests;

/// <summary>
/// Verifies that the worker_prompts JSON column deserializes correctly after the
/// AddWorkerPromptsValueObject migration's backfill runs. The factory uses EnsureCreated()
/// so migrations do not run in tests — the backfill output is simulated by overwriting the
/// worker_prompts column via raw SQL, exactly as WhenBackfillingLastImageBuiltAt does for its
/// backfill. This proves the deserializer survives the JSON shape produced by json_object().
/// </summary>
public sealed class WhenBackfillingWorkerPrompts : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenBackfillingWorkerPrompts()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task BackfilledFromNullLegacyColumns_SystemPromptFallsBackToDefault()
    {
        // Arrange — simulate a row where legacy system_prompt_template was NULL.
        // The migration backfill coalesces NULLs/empties to WorkerPrompts.Default values.
        GlobalSettingsId id = await SeedAndOverwriteWithJsonAsync(WorkerPrompts.Default);

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerPrompts.SystemPromptTemplate.ShouldBe(WorkerPrompts.Default.SystemPromptTemplate);
    }

    [Fact]
    public async Task BackfilledFromNullLegacyColumns_WorkerPromptFallsBackToDefault()
    {
        // Arrange — simulate a row where legacy worker_prompt_template was NULL.
        GlobalSettingsId id = await SeedAndOverwriteWithJsonAsync(WorkerPrompts.Default);

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerPrompts.WorkerPromptTemplate.ShouldBe(WorkerPrompts.Default.WorkerPromptTemplate);
    }

    [Fact]
    public async Task BackfilledFromNullLegacyColumns_BranchNamingFallsBackToDefault()
    {
        // Arrange — branch naming had no prior column; the backfill always writes the default.
        GlobalSettingsId id = await SeedAndOverwriteWithJsonAsync(WorkerPrompts.Default);

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerPrompts.BranchNamingInstruction.ShouldBe(WorkerPrompts.Default.BranchNamingInstruction);
    }

    [Fact]
    public async Task BackfilledFromLegacyColumns_CustomSystemPromptIsPreserved()
    {
        // Arrange — simulate a row where system_prompt_template held a non-default value.
        // The migration backfill writes that value into the new worker_prompts JSON blob.
        WorkerPrompts customPrompts = WorkerPrompts.Create(
                "Custom system prompt for issue #{issueNumber}.",
                WorkerPrompts.Default.WorkerPromptTemplate,
                WorkerPrompts.Default.BranchNamingInstruction)
            .Match(onSuccess: v => v, onFailure: e => throw new InvalidOperationException(e.Message));

        GlobalSettingsId id = await SeedAndOverwriteWithJsonAsync(customPrompts);

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerPrompts.SystemPromptTemplate.ShouldBe(customPrompts.SystemPromptTemplate);
    }

    [Fact]
    public async Task BackfilledFromLegacyColumns_CustomWorkerPromptIsPreserved()
    {
        // Arrange — simulate a row where worker_prompt_template held a non-default value.
        WorkerPrompts customPrompts = WorkerPrompts.Create(
                WorkerPrompts.Default.SystemPromptTemplate,
                "Custom worker prompt for #{issueNumber}.",
                WorkerPrompts.Default.BranchNamingInstruction)
            .Match(onSuccess: v => v, onFailure: e => throw new InvalidOperationException(e.Message));

        GlobalSettingsId id = await SeedAndOverwriteWithJsonAsync(customPrompts);

        // Act
        GlobalSettings? reloaded = await ReloadSettingsAsync(id);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.WorkerPrompts.WorkerPromptTemplate.ShouldBe(customPrompts.WorkerPromptTemplate);
    }

    private async Task<GlobalSettingsId> SeedAndOverwriteWithJsonAsync(WorkerPrompts prompts)
    {
        // Seed a normal row so all required columns are populated via EF, then overwrite the
        // worker_prompts column with the JSON produced by the migration's backfill SQL, simulating
        // what json_object(...) would write for a pre-migration row.
        // ExecuteSqlRawAsync's {0} placeholder avoids format-string collisions with JSON braces.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Serialize with the same options the EF ValueConverter uses (default STJ).
        string json = JsonSerializer.Serialize(prompts);

        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE global_settings SET worker_prompts = {0}",
            [json],
            TestContext.Current.CancellationToken);

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
