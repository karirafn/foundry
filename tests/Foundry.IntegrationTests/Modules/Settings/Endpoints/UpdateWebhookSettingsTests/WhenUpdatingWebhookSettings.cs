using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
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
    private const string StoredUrl = "https://hooks.example.com";
    private const int WebhookPort = 6123;

    private readonly StubWebhookIngressProbe _probe = new(WebhookPort, new WebhookProbeResult.Reached());
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenUpdatingWebhookSettings()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services => services.AddSingleton<IWebhookIngressProbe>(_probe));
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedAsync(string? publicBaseUrl = null)
    {
        // SettingsSeeder is a hosted service and is removed in tests — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        GlobalSettings settings = GlobalSettings.Create();
        if (publicBaseUrl is not null)
        {
            settings.UpdateWebhookSettings(publicBaseUrl, 15);
        }

        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string?> LoadStoredUrlAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        GlobalSettings settings = await dbContext.Set<GlobalSettings>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        return settings.WebhookPublicBaseUrl?.Value;
    }

    private Task<HttpResponseMessage> PutAsync(string? url, int backstop) =>
        _client.PutAsJsonAsync(
            new Uri("/api/settings/webhooks", UriKind.Relative),
            new { webhookPublicBaseUrl = url, backstopIntervalMinutes = backstop },
            TestContext.Current.CancellationToken);

    private static async Task<GlobalSettingsSummary> ReadSummaryAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<GlobalSettingsSummary>(
            FoundryWebAppFactory.JsonOptions,
            TestContext.Current.CancellationToken)).ShouldNotBeNull();

    private static async Task<ProblemDetails> ReadProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken)).ShouldNotBeNull();

    [Fact]
    public async Task WhenValid_PersistsAndReturnsSummary()
    {
        // Arrange
        await SeedAsync();

        // Act
        HttpResponseMessage response = await PutAsync("https://hooks.example.com/", 30);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary summary = await ReadSummaryAsync(response);
        summary.WebhookPublicBaseUrl.ShouldBe("https://hooks.example.com");
        summary.BackstopIntervalMinutes.ShouldBe(30);
        summary.WebhookPort.ShouldBe(WebhookPort);
    }

    [Fact]
    public async Task WhenUrlChanged_ProbesTheNormalizedUrlBeforeSaving()
    {
        // Arrange
        await SeedAsync(StoredUrl);

        // Act
        HttpResponseMessage response = await PutAsync(" https://new.example.com/ ", 15);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _probe.VerifiedUrls.ShouldBe(["https://new.example.com"]);
        (await LoadStoredUrlAsync()).ShouldBe("https://new.example.com");
    }

    [Theory]
    [InlineData("http://hooks.example.com", 15, "Settings.InvalidWebhookPublicBaseUrl")]
    [InlineData("not a url", 15, "Settings.InvalidWebhookPublicBaseUrl")]
    [InlineData("https://localhost:5099", 15, "Settings.InvalidWebhookPublicBaseUrl")]
    [InlineData(null, 15, "Settings.InvalidWebhookPublicBaseUrl")]
    [InlineData("  ", 15, "Settings.InvalidWebhookPublicBaseUrl")]
    [InlineData(null, 1, "Settings.InvalidBackstopInterval")]
    public async Task WhenInvalid_Returns400WithFieldKeyedProblemTypeAndNoProbe(string? url, int backstop, string code)
    {
        // Arrange
        await SeedAsync();

        // Act
        HttpResponseMessage response = await PutAsync(url, backstop);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).Type.ShouldEndWith(code);
        _probe.VerifiedUrls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenUrlBlankAndOneIsStored_KeepsTheStoredUrl()
    {
        // Arrange
        await SeedAsync(StoredUrl);

        // Act
        HttpResponseMessage response = await PutAsync("", 15);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).Detail.ShouldBe("Enter the public https URL your tunnel or proxy serves.");
        (await LoadStoredUrlAsync()).ShouldBe(StoredUrl);
    }

    [Fact]
    public async Task WhenUrlEqualsStored_SavesBackstopWithoutProbing()
    {
        // Arrange
        await SeedAsync(StoredUrl);

        // Act
        HttpResponseMessage response = await PutAsync(StoredUrl + "/", 45);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadSummaryAsync(response)).BackstopIntervalMinutes.ShouldBe(45);
        _probe.VerifiedUrls.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetSettings_CarriesTheWebhookPort()
    {
        // Arrange
        await SeedAsync();

        // Act
        GlobalSettingsSummary summary = (await _client.GetFromJsonAsync<GlobalSettingsSummary>(
            new Uri("/api/settings", UriKind.Relative),
            FoundryWebAppFactory.JsonOptions,
            TestContext.Current.CancellationToken)).ShouldNotBeNull();

        // Assert
        summary.WebhookPort.ShouldBe(WebhookPort);
    }
}
