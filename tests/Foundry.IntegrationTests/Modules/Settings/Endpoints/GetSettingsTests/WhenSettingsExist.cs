using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.GetSettingsTests;

public sealed class WhenSettingsExist : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenSettingsExist()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedDefaultSettingsAsync()
    {
        // SettingsSeeder is a hosted service and is removed in tests — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsOkWithDefaultValues()
    {
        // Arrange
        await SeedDefaultSettingsAsync();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.MaxConcurrent.ShouldBe(1),
            () => summary.TimeoutMinutes.ShouldBe(120),
            () => summary.PollIntervalSeconds.ShouldBe(30));
    }

    [Fact]
    public async Task ReturnsPromptTemplatesWhenSet()
    {
        // Arrange
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdatePromptTemplates("system-prompt", "worker-prompt");
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.SystemPromptTemplate.ShouldBe("system-prompt"),
            () => summary.WorkerPromptTemplate.ShouldBe("worker-prompt"));
    }

    [Fact]
    public async Task ReturnsNullPromptTemplatesByDefault()
    {
        // Arrange
        await SeedDefaultSettingsAsync();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings", UriKind.Relative),
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
    public async Task SerializesImageBuildStatusAsString()
    {
        // Arrange
        // ReadFromJsonAsync<GlobalSettingsSummary> deserializes both "2" and "Failed" identically,
        // so a typed round-trip cannot catch the regression — read the raw JSON and assert the
        // token kind and value directly (design decision D3).
        // DbContext seeding is used because no HTTP endpoint produces the Failed state directly.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        settings.FailImageBuild("build error tail", nextRetryAt: null, attempt: 0);
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string rawJson = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument document = JsonDocument.Parse(rawJson);
        JsonElement imageBuildStatusElement = document.RootElement.GetProperty("imageBuildStatus");
        imageBuildStatusElement.ValueKind.ShouldBe(JsonValueKind.String);
        imageBuildStatusElement.GetString().ShouldBe("Failed");
    }

    [Fact]
    public async Task ExposesNextRetryAtInRawJsonWhenImageBuildFailed()
    {
        // Arrange
        // DbContext seeding is used because no HTTP endpoint produces the Failed state directly.
        // A typed round-trip cannot distinguish a missing field from a null one, so the raw JSON
        // is asserted directly to prove the camelCase property name and value are present (D2).
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DateTimeOffset expectedNextRetryAt = new(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
        GlobalSettings settings = GlobalSettings.Create();
        settings.FailImageBuild("error tail", nextRetryAt: expectedNextRetryAt, attempt: 2);
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string rawJson = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument document = JsonDocument.Parse(rawJson);
        JsonElement nextRetryAtElement = document.RootElement.GetProperty("nextRetryAt");
        nextRetryAtElement.ValueKind.ShouldBe(JsonValueKind.String);
        nextRetryAtElement.GetDateTimeOffset().ShouldBe(expectedNextRetryAt);
    }

    [Fact]
    public async Task ExposesAttemptInRawJsonWhenImageBuildFailed()
    {
        // Arrange
        // DbContext seeding is used because no HTTP endpoint produces the Failed state directly.
        // A typed round-trip cannot distinguish absence from 0, so the raw JSON is asserted
        // directly to prove the camelCase property name and value are present (D2).
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        settings.FailImageBuild("error tail", nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(5), attempt: 3);
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string rawJson = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument document = JsonDocument.Parse(rawJson);
        JsonElement attemptElement = document.RootElement.GetProperty("attempt");
        attemptElement.ValueKind.ShouldBe(JsonValueKind.Number);
        attemptElement.GetInt32().ShouldBe(3);
    }
}
