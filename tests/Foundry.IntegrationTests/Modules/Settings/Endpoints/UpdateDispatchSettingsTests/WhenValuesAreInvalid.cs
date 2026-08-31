using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateDispatchSettingsTests;

public sealed class WhenValuesAreInvalid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenValuesAreInvalid()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenProbeIntervalIsBelowMin_ReturnsBadRequest()
    {
        // Arrange
        object body = new { autoResumeOnUsageReset = true, probeIntervalMinutes = 4, pollIntervalSeconds = 30 };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/dispatch", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenProbeIntervalIsZero_ReturnsBadRequest()
    {
        // Arrange
        object body = new { autoResumeOnUsageReset = true, probeIntervalMinutes = 0, pollIntervalSeconds = 30 };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/dispatch", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenPollIntervalIsBelowMin_ReturnsBadRequest()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new
        {
            autoResumeOnUsageReset = true,
            probeIntervalMinutes = 30,
            pollIntervalSeconds = GlobalSettings.MinPollIntervalSeconds - 1,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/dispatch", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenPollIntervalIsAboveMax_ReturnsBadRequest()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new
        {
            autoResumeOnUsageReset = true,
            probeIntervalMinutes = 30,
            pollIntervalSeconds = GlobalSettings.MaxPollIntervalSeconds + 1,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/dispatch", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
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
}
