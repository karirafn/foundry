using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdatePromptTemplatesTests;

public sealed class WhenTemplatesAreCleared : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTemplatesAreCleared()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedSettingsWithTemplatesAsync()
    {
        // SettingsSeeder is a hosted service and is removed in tests — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdatePromptTemplates("Existing system prompt.", "Existing worker prompt.");
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenBothTemplatesAreNull_ReturnsOkWithNullTemplates()
    {
        // Arrange
        await SeedSettingsWithTemplatesAsync();
        object body = new
        {
            systemPromptTemplate = (string?)null,
            workerPromptTemplate = (string?)null,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/prompts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.SystemPromptTemplate.ShouldBeNull(),
            () => summary.WorkerPromptTemplate.ShouldBeNull());
    }

    [Fact]
    public async Task WhenOnlySystemPromptTemplateIsNull_ClearsSystemPromptAndRetainsWorkerPrompt()
    {
        // Arrange
        await SeedSettingsWithTemplatesAsync();
        object body = new
        {
            systemPromptTemplate = (string?)null,
            workerPromptTemplate = "Retained worker prompt.",
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/prompts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.SystemPromptTemplate.ShouldBeNull(),
            () => summary.WorkerPromptTemplate.ShouldBe("Retained worker prompt."));
    }

    [Fact]
    public async Task WhenOnlyWorkerPromptTemplateIsNull_ClearsWorkerPromptAndRetainsSystemPrompt()
    {
        // Arrange
        await SeedSettingsWithTemplatesAsync();
        object body = new
        {
            systemPromptTemplate = "Retained system prompt.",
            workerPromptTemplate = (string?)null,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/prompts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.SystemPromptTemplate.ShouldBe("Retained system prompt."),
            () => summary.WorkerPromptTemplate.ShouldBeNull());
    }
}
