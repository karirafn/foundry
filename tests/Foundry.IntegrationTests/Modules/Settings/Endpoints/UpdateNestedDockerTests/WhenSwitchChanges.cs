using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared.Infrastructure.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateNestedDockerTests;

public sealed class WhenSwitchChanges : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenSwitchChanges()
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

        dbContext.Set<GlobalSettings>().Add(GlobalSettings.Create());
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> PutAsync(bool enabled)
        => await _client.PutAsJsonAsync(
            new Uri("/api/settings/nested-docker", UriKind.Relative),
            new { enabled },
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task ReturnsOkWithTheSwitchOn()
    {
        // Arrange
        await SeedDefaultSettingsAsync();

        // Act
        HttpResponseMessage response = await PutAsync(enabled: true);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull().NestedDockerEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task StartsNoRebuild()
    {
        // Arrange
        await SeedDefaultSettingsAsync();

        // Act
        HttpResponseMessage response = await PutAsync(enabled: true);

        // Assert — no WorkerImageSettingsChanged (or any other event) reaches the outbox
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetSettingsReflectsTheSavedSwitch()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        await PutAsync(enabled: true);

        // Act
        GlobalSettingsSummary? summary = await _client.GetFromJsonAsync<GlobalSettingsSummary>(
            new Uri("/api/settings", UriKind.Relative),
            FoundryWebAppFactory.JsonOptions,
            TestContext.Current.CancellationToken);

        // Assert
        summary.ShouldNotBeNull().NestedDockerEnabled.ShouldBeTrue();
    }
}
