using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
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
            .ReadFromJsonAsync<GlobalSettingsSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.MaxConcurrent.ShouldBe(1),
            () => summary.TimeoutMinutes.ShouldBe(120),
            () => summary.AccessTokenPresent.ShouldBeFalse(),
            () => summary.RefreshTokenPresent.ShouldBeFalse(),
            () => summary.ExpiresAt.ShouldBeNull(),
            () => summary.SubscriptionType.ShouldBeNull());
    }

    [Fact]
    public async Task ReturnsApiKeyAuthModeByDefault()
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
            .ReadFromJsonAsync<GlobalSettingsSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.AuthMode.ShouldBe("ApiKey");
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
            .ReadFromJsonAsync<GlobalSettingsSummary>(TestContext.Current.CancellationToken);
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
            .ReadFromJsonAsync<GlobalSettingsSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.SystemPromptTemplate.ShouldBeNull(),
            () => summary.WorkerPromptTemplate.ShouldBeNull());
    }
}
