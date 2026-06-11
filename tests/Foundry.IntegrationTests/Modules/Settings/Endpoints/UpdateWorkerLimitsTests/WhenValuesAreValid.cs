using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateWorkerLimitsTests;

public sealed class WhenValuesAreValid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenValuesAreValid()
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
    public async Task ReturnsOkWithUpdatedLimits()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new { maxConcurrent = 5, timeoutMinutes = 90 };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/limits", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.MaxConcurrent.ShouldBe(5),
            () => summary.TimeoutMinutes.ShouldBe(90));
    }

    [Fact]
    public async Task SubsequentGetReturnsUpdatedLimits()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new { maxConcurrent = 10, timeoutMinutes = 180 };

        await _client.PutAsJsonAsync(
            new Uri("/api/settings/limits", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

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
            () => summary.MaxConcurrent.ShouldBe(10),
            () => summary.TimeoutMinutes.ShouldBe(180));
    }
}
