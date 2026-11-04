using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateWebhookSettingsTests;

public sealed class WhenUpdatingWebhookSettings : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory = new();
    private readonly HttpClient _client;

    public WhenUpdatingWebhookSettings()
    {
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedAsync()
    {
        // SettingsSeeder is a hosted service and is removed in tests — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        dbContext.Set<GlobalSettings>().Add(GlobalSettings.Create());
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenValid_PersistsAndReturnsSummary()
    {
        // Arrange
        await SeedAsync();

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/webhooks", UriKind.Relative),
            new { webhookPublicBaseUrl = "https://hooks.example.com/", backstopIntervalMinutes = 30 },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary summary = (await response.Content.ReadFromJsonAsync<GlobalSettingsSummary>(
            FoundryWebAppFactory.JsonOptions,
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
        summary.WebhookPublicBaseUrl.ShouldBe("https://hooks.example.com");
        summary.BackstopIntervalMinutes.ShouldBe(30);
    }

    [Theory]
    [InlineData("http://hooks.example.com", 15, "Settings.InvalidWebhookPublicBaseUrl")]
    [InlineData("not a url", 15, "Settings.InvalidWebhookPublicBaseUrl")]
    [InlineData(null, 1, "Settings.InvalidBackstopInterval")]
    public async Task WhenInvalid_Returns400WithFieldKeyedProblemType(string? url, int backstop, string code)
    {
        // Arrange
        await SeedAsync();

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/webhooks", UriKind.Relative),
            new { webhookPublicBaseUrl = url, backstopIntervalMinutes = backstop },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
        problem.Type.ShouldEndWith(code);
    }
}
